using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Donkeh.Inspector;

/// <summary>
/// Small HTTP server used by the Deploy tab to download build outputs from a
/// browser on the same machine or on the local network.
/// </summary>
/// <remarks>
/// <para>
/// The server binds all IPv4 interfaces so both <c>localhost</c> and the
/// machine's internal address work. It serves the directory supplied through
/// <see cref="RootDirectory"/>; the root page lists build artifacts and lets the
/// browser walk folders, and any other path downloads the matching file.
/// </para>
/// <para>
/// The same page also accepts screenshots. A <c>POST /upload?name=...</c> request
/// writes the raw image body into <see cref="UploadDirectory"/>, normally the
/// application's <c>debug</c> folder, so screenshots taken in a browser become
/// files the application and its tooling can read.
/// </para>
/// <para>
/// The implementation uses a raw <see cref="TcpListener"/> instead of
/// <see cref="HttpListener"/> so it works without a Windows URL reservation or
/// administrator rights. It supports <c>GET</c>, <c>HEAD</c> and the upload
/// <c>POST</c>, streams large files, rejects path traversal and closes each
/// connection after one request. The server has no authentication; it is
/// intended for a trusted local network.
/// </para>
/// </remarks>
public sealed class DownloadServer : IDisposable
{
    /// <summary>Notifies the inspector that the server state changed.</summary>
    private readonly Action _changed;

    /// <summary>Guards the listener and cancellation fields.</summary>
    private readonly object _gate = new();

    /// <summary>Serializes name allocation and writing for simultaneous uploads.</summary>
    private readonly object _uploadGate = new();

    /// <summary>The active listener, or null while stopped.</summary>
    private TcpListener? _listener;

    /// <summary>Cancels the accept loop and client handlers.</summary>
    private CancellationTokenSource? _cancellation;

    /// <summary>Whether the accept loop is running.</summary>
    private volatile bool _running;

    /// <summary>Backing field for <see cref="Port"/>.</summary>
    private int _port = 8080;

    /// <summary>Backing field for <see cref="RootDirectory"/>.</summary>
    private string? _rootDirectory;

    /// <summary>Backing field for <see cref="UploadDirectory"/>.</summary>
    private string? _uploadDirectory;

    /// <summary>Backing field for <see cref="LastError"/>.</summary>
    private string? _lastError;

    /// <summary>Largest upload body accepted, in bytes.</summary>
    private const long MaxUploadBytes = 32L * 1024 * 1024;

    /// <summary>Image extensions accepted by <c>POST /upload</c>.</summary>
    private static readonly string[] UploadExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".avif", ".tif", ".tiff"];

    /// <summary>Addresses printed by the UI while the server is running.</summary>
    private IReadOnlyList<string> _urls = [];

    /// <summary>Creates a stopped server.</summary>
    /// <param name="changed">
    /// Invoked whenever the running state, port, root or error changes. The call
    /// may happen on a worker thread.
    /// </param>
    public DownloadServer(Action changed)
    {
        _changed = changed;
    }

    /// <summary>Gets a value indicating whether the server is listening.</summary>
    public bool IsRunning => _running;

    /// <summary>Gets or sets the loopback port.</summary>
    /// <value>
    /// A port in 1..65535. Values outside that range are ignored. Defaults to
    /// 8080.
    /// </value>
    /// <remarks>
    /// The port can only change while the server is stopped; the setter ignores
    /// changes in the running state because they would leave the listener bound
    /// to the old port.
    /// </remarks>
    public int Port
    {
        get => _port;
        set
        {
            if (value is < 1 or > 65535 || _running || value == _port)
            {
                return;
            }

            _port = value;
            _changed();
        }
    }

    /// <summary>Gets or sets the directory whose files are served.</summary>
    /// <remarks>
    /// Changing the root stops a running server. The directory is created when
    /// the server starts, so an empty build folder still produces an index.
    /// </remarks>
    public string? RootDirectory
    {
        get => _rootDirectory;
        set
        {
            if (string.Equals(_rootDirectory, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _rootDirectory = value;

            if (_running)
            {
                Stop();
            }
            else
            {
                _changed();
            }
        }
    }

    /// <summary>Gets or sets the directory that receives uploaded screenshots.</summary>
    /// <remarks>
    /// Normally the application's <c>debug</c> folder. The directory is created
    /// on demand when the first image arrives, so it does not need to exist when
    /// the server starts. A null or empty value disables uploads while leaving
    /// downloads available.
    /// </remarks>
    public string? UploadDirectory
    {
        get => _uploadDirectory;
        set
        {
            if (string.Equals(_uploadDirectory, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _uploadDirectory = value;
            _changed();
        }
    }

    /// <summary>Gets the last start or request error, if any.</summary>
    public string? LastError => _lastError;

    /// <summary>Gets the URL for this machine's loopback interface.</summary>
    public string Url => "http://localhost:" + _port.ToString(CultureInfo.InvariantCulture) + "/";

    /// <summary>Gets every advertised address while the server is running.</summary>
    /// <value>
    /// The loopback URL followed by one URL per active IPv4 interface, or an
    /// empty list while stopped.
    /// </value>
    /// <remarks>
    /// The list is captured when the server starts. A DHCP address change while
    /// it is running is only picked up after a stop/start cycle.
    /// </remarks>
    public IReadOnlyList<string> Urls => _urls;

    /// <summary>Starts the server if stopped, or stops it if running.</summary>
    public void Toggle()
    {
        if (_running)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    /// <summary>Starts listening on all IPv4 interfaces.</summary>
    /// <remarks>
    /// Binding failures (for example a port already in use) are reported through
    /// <see cref="LastError"/> instead of throwing, so the inspector remains
    /// usable. Opening the port may require allowing the application through the
    /// host firewall before other machines can connect.
    /// </remarks>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        var root = _rootDirectory;

        if (string.IsNullOrWhiteSpace(root))
        {
            SetError("No builds directory is available.");
            return;
        }

        try
        {
            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return;
        }

        try
        {
            var listener = new TcpListener(IPAddress.Any, _port);
            listener.Start();

            var urls = BuildUrls(_port);
            var cancellation = new CancellationTokenSource();

            lock (_gate)
            {
                _listener = listener;
                _cancellation = cancellation;
                _urls = urls;
                _lastError = null;
                _running = true;
            }

            Console.WriteLine("[Donkeh] Download server: " + string.Join("  ", urls));
            _changed();

            _ = Task.Run(() => AcceptLoopAsync(listener, root, cancellation.Token));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    /// <summary>Stops listening and releases the port.</summary>
    public void Stop()
    {
        TcpListener? listener;
        CancellationTokenSource? cancellation;

        lock (_gate)
        {
            listener = _listener;
            cancellation = _cancellation;
            _listener = null;
            _cancellation = null;
            _urls = [];
            _running = false;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (Exception)
        {
            // Cancellation is best-effort; closing the listener also ends the loop.
        }

        try
        {
            listener?.Stop();
        }
        catch (Exception)
        {
            // The socket may already be closed.
        }

        cancellation?.Dispose();
        _changed();
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    /// <summary>Records an error and publishes it.</summary>
    /// <param name="message">The error text.</param>
    private void SetError(string message)
    {
        _lastError = message;

        lock (_gate)
        {
            _urls = [];
            _running = false;
        }

        _changed();
    }

    /// <summary>Builds the loopback and internal URLs for a port.</summary>
    /// <param name="port">The bound port.</param>
    /// <returns>The advertised addresses, loopback first.</returns>
    /// <remarks>
    /// IPv4 addresses on operational, non-loopback interfaces are included,
    /// except link-local (169.254.x.x) addresses, which are not reachable from
    /// another machine on the normal LAN.
    /// </remarks>
    private static IReadOnlyList<string> BuildUrls(int port)
    {
        var portText = port.ToString(CultureInfo.InvariantCulture);
        var urls = new List<string> { "http://localhost:" + portText + "/" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                IPInterfaceProperties properties;

                try
                {
                    properties = adapter.GetIPProperties();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var unicast in properties.UnicastAddresses)
                {
                    var address = unicast.Address;

                    if (address.AddressFamily != AddressFamily.InterNetwork
                        || IPAddress.IsLoopback(address))
                    {
                        continue;
                    }

                    var text = address.ToString();

                    if (text.StartsWith("169.254.", StringComparison.Ordinal)
                        || !seen.Add(text))
                    {
                        continue;
                    }

                    urls.Add("http://" + text + ":" + portText + "/");
                }
            }
        }
        catch (Exception)
        {
            // Network information is best-effort; loopback always remains.
        }

        return urls;
    }

    /// <summary>Accepts connections until the server stops.</summary>
    /// <param name="listener">The active listener.</param>
    /// <param name="root">The absolute directory being served.</param>
    /// <param name="cancellation">Cancellation token for shutdown.</param>
    /// <returns>A task that completes when the listener closes.</returns>
    private async Task AcceptLoopAsync(TcpListener listener, string root, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, root, cancellation), CancellationToken.None);
        }
    }

    /// <summary>Handles one HTTP request and closes the connection.</summary>
    /// <param name="client">The accepted TCP client.</param>
    /// <param name="root">The served directory.</param>
    /// <param name="cancellation">Cancellation token for shutdown.</param>
    /// <returns>A task that completes after the response is written.</returns>
    private async Task HandleClientAsync(
        TcpClient client,
        string root,
        CancellationToken cancellation)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;

                await using var stream = client.GetStream();
                var head = await ReadHeadAsync(stream, cancellation).ConfigureAwait(false);

                if (string.IsNullOrEmpty(head))
                {
                    return;
                }

                var lines = head.Split("\r\n", StringSplitOptions.None);
                var requestLine = lines[0];
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (var i = 1; i < lines.Length; i++)
                {
                    var separator = lines[i].IndexOf(':');

                    if (separator <= 0)
                    {
                        continue;
                    }

                    headers[lines[i][..separator].Trim()] = lines[i][(separator + 1)..].Trim();
                }

                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                {
                    await WriteStatusAsync(stream, 400, "Bad Request", headOnly: false, cancellation)
                        .ConfigureAwait(false);
                    return;
                }

                var method = parts[0];
                var headOnly = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
                var isGet = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
                var isPost = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);

                if (!isGet && !isPost && !headOnly)
                {
                    await WriteStatusAsync(stream, 405, "Method Not Allowed", headOnly: false, cancellation)
                        .ConfigureAwait(false);
                    return;
                }

                var target = parts[1];
                var queryIndex = target.IndexOf('?');
                var query = queryIndex >= 0 ? target[(queryIndex + 1)..] : string.Empty;

                if (queryIndex >= 0)
                {
                    target = target[..queryIndex];
                }

                var relative = Uri.UnescapeDataString(target).TrimStart('/');

                if (isPost)
                {
                    if (string.Equals(relative, "upload", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleUploadAsync(stream, query, headers, cancellation).ConfigureAwait(false);
                        return;
                    }

                    await WriteStatusAsync(stream, 405, "Method Not Allowed", headOnly: false, cancellation)
                        .ConfigureAwait(false);
                    return;
                }

                var fullPath = Path.GetFullPath(
                    relative.Length == 0 ? root : Path.Combine(root, relative));

                if (!IsUnderRoot(root, fullPath))
                {
                    await WriteStatusAsync(stream, 403, "Forbidden", headOnly, cancellation)
                        .ConfigureAwait(false);
                    return;
                }

                if (Directory.Exists(fullPath))
                {
                    var html = BuildIndex(root, fullPath);
                    await WriteBodyAsync(
                            stream,
                            200,
                            "OK",
                            "text/html; charset=utf-8",
                            Encoding.UTF8.GetBytes(html),
                            extraHeaders: null,
                            headOnly,
                            cancellation)
                        .ConfigureAwait(false);
                    return;
                }

                if (!File.Exists(fullPath))
                {
                    await WriteStatusAsync(stream, 404, "Not Found", headOnly, cancellation)
                        .ConfigureAwait(false);
                    return;
                }

                var file = new FileInfo(fullPath);
                var disposition = "Content-Disposition: attachment; filename=\"" +
                    EscapeHeader(file.Name) + "\"\r\n";

                await WriteFileAsync(stream, file, disposition, headOnly, cancellation)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The server is stopping.
            }
            catch (Exception)
            {
                // A malformed request or a disconnected client should not take
                // down the accept loop.
            }
        }
    }

    /// <summary>Reads request headers without consuming any of the body.</summary>
    /// <param name="stream">Client stream.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The request head, or null when the peer closed immediately.</returns>
    /// <remarks>
    /// Reads one byte at a time because a buffered reader would swallow the
    /// beginning of an upload body. Headers are tiny, so the extra reads are
    /// negligible compared with the image transfer.
    /// </remarks>
    private static async Task<string?> ReadHeadAsync(
        NetworkStream stream,
        CancellationToken cancellation)
    {
        var buffer = new byte[1];
        var head = new StringBuilder(1024);
        var matched = 0;

        while (head.Length < 64 * 1024)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellation).ConfigureAwait(false);

            if (read == 0)
            {
                return head.Length == 0 ? null : head.ToString();
            }

            var c = (char)buffer[0];
            head.Append(c);

            if (c == '\r')
            {
                matched = matched is 0 or 2 ? matched + 1 : 0;
            }
            else if (c == '\n')
            {
                matched = matched is 1 or 3 ? matched + 1 : 0;
            }
            else
            {
                matched = 0;
            }

            if (matched == 4)
            {
                return head.ToString();
            }
        }

        return head.ToString();
    }

    /// <summary>Handles one screenshot upload request.</summary>
    /// <param name="stream">Client stream positioned at the request body.</param>
    /// <param name="query">Raw query string from the request target.</param>
    /// <param name="headers">Parsed request headers.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task that completes after the response is written.</returns>
    /// <remarks>
    /// The body is the raw image; the original file name travels in the
    /// <c>name</c> query parameter. Keeping the wire format this simple lets the
    /// generated page use <c>fetch</c> without building a multipart encoder.
    /// </remarks>
    private async Task HandleUploadAsync(
        NetworkStream stream,
        string query,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(_uploadDirectory))
        {
            await WriteJsonErrorAsync(
                    stream,
                    503,
                    "Service Unavailable",
                    "Uploads are not configured.",
                    cancellation)
                .ConfigureAwait(false);
            return;
        }

        if (!headers.TryGetValue("Content-Length", out var lengthText)
            || !long.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
            || length < 0)
        {
            await WriteJsonErrorAsync(
                    stream,
                    411,
                    "Length Required",
                    "An upload needs a Content-Length header.",
                    cancellation)
                .ConfigureAwait(false);
            return;
        }

        if (length > MaxUploadBytes)
        {
            await WriteJsonErrorAsync(
                    stream,
                    413,
                    "Payload Too Large",
                    "The image is larger than 32 MB.",
                    cancellation)
                .ConfigureAwait(false);
            return;
        }

        var body = new byte[(int)length];
        var offset = 0;

        while (offset < body.Length)
        {
            var read = await stream
                .ReadAsync(body.AsMemory(offset, body.Length - offset), cancellation)
                .ConfigureAwait(false);

            if (read == 0)
            {
                await WriteJsonErrorAsync(
                        stream,
                        400,
                        "Bad Request",
                        "The upload ended before the declared length.",
                        cancellation)
                    .ConfigureAwait(false);
                return;
            }

            offset += read;
        }

        headers.TryGetValue("Content-Type", out var contentType);
        var fileName = BuildUploadFileName(GetQueryValue(query, "name"), contentType);
        var extension = Path.GetExtension(fileName);

        if (!UploadExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            await WriteJsonErrorAsync(
                    stream,
                    415,
                    "Unsupported Media Type",
                    "Only image uploads are accepted (png, jpg, gif, webp, bmp, svg, avif, tif).",
                    cancellation)
                .ConfigureAwait(false);
            return;
        }

        string directory;

        try
        {
            directory = Path.GetFullPath(_uploadDirectory);
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            await WriteJsonErrorAsync(
                    stream,
                    500,
                    "Internal Server Error",
                    "Could not create the upload directory: " + ex.Message,
                    cancellation)
                .ConfigureAwait(false);
            return;
        }

        string destination;

        try
        {
            // Allocate the name and write under one lock so two files dropped
            // together cannot pick the same path and overwrite each other.
            lock (_uploadGate)
            {
                destination = GetUniquePath(directory, fileName);
                File.WriteAllBytes(destination, body);
            }
        }
        catch (Exception ex)
        {
            await WriteJsonErrorAsync(
                    stream,
                    500,
                    "Internal Server Error",
                    "Could not save the image: " + ex.Message,
                    cancellation)
                .ConfigureAwait(false);
            return;
        }

        var relativePath = Path.GetRelativePath(directory, destination).Replace('\\', '/');
        var json = JsonSerializer.Serialize(new
        {
            ok = true,
            name = Path.GetFileName(destination),
            path = relativePath
        });

        await WriteBodyAsync(
                stream,
                200,
                "OK",
                "application/json; charset=utf-8",
                Encoding.UTF8.GetBytes(json),
                extraHeaders: null,
                headOnly: false,
                cancellation)
            .ConfigureAwait(false);

        _changed();
    }

    /// <summary>Writes a JSON <c>{ ok: false }</c> response.</summary>
    /// <param name="stream">Client stream.</param>
    /// <param name="status">HTTP status code.</param>
    /// <param name="reason">HTTP reason phrase.</param>
    /// <param name="message">Error text shown to the browser.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task that completes after the response is written.</returns>
    private static Task WriteJsonErrorAsync(
        NetworkStream stream,
        int status,
        string reason,
        string message,
        CancellationToken cancellation)
    {
        var json = JsonSerializer.Serialize(new { ok = false, error = message });

        return WriteBodyAsync(
            stream,
            status,
            reason,
            "application/json; charset=utf-8",
            Encoding.UTF8.GetBytes(json),
            extraHeaders: null,
            headOnly: false,
            cancellation);
    }

    /// <summary>Reads one value from a raw query string.</summary>
    /// <param name="query">Query text without the leading <c>?</c>.</param>
    /// <param name="key">Case-insensitive key to look for.</param>
    /// <returns>The decoded value, or null when absent or malformed.</returns>
    private static string? GetQueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];

            if (!string.Equals(Decode(name), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return separator < 0 ? string.Empty : Decode(pair[(separator + 1)..]);
        }

        return null;
    }

    /// <summary>Decodes a percent-encoded query component.</summary>
    /// <param name="value">Encoded text.</param>
    /// <returns>The decoded text, or the original text when decoding fails.</returns>
    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (Exception)
        {
            return value;
        }
    }

    /// <summary>Produces a safe, image-extension file name for an upload.</summary>
    /// <param name="requested">File name supplied by the browser, if any.</param>
    /// <param name="contentType">Request <c>Content-Type</c>, if any.</param>
    /// <returns>A file name with no directory components.</returns>
    private static string BuildUploadFileName(string? requested, string? contentType)
    {
        var candidate = (requested ?? string.Empty).Replace('\\', '/');
        candidate = Path.GetFileName(candidate);
        var extension = Path.GetExtension(candidate);

        if (extension.Length == 0)
        {
            extension = ExtensionForContentType(contentType);
        }

        var stem = extension.Length > 0
            ? Path.GetFileNameWithoutExtension(candidate)
            : candidate;

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "screenshot";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new StringBuilder(stem.Length);

        foreach (var c in stem)
        {
            if (Array.IndexOf(invalid, c) < 0)
            {
                cleaned.Append(c);
            }
        }

        var safeStem = cleaned.ToString().Trim().Trim('.', ' ');

        if (safeStem.Length == 0)
        {
            safeStem = "screenshot";
        }

        if (safeStem.Length > 96)
        {
            safeStem = safeStem[..96];
        }

        return safeStem + extension.ToLowerInvariant();
    }

    /// <summary>Maps an upload content type to a file extension.</summary>
    /// <param name="contentType">Request content type, possibly with parameters.</param>
    /// <returns>A file extension, or an empty string when unknown.</returns>
    private static string ExtensionForContentType(string? contentType)
    {
        var mediaType = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

        return mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            "image/avif" => ".avif",
            "image/tiff" => ".tiff",
            _ => string.Empty
        };
    }

    /// <summary>Finds an unused path for a new upload.</summary>
    /// <param name="directory">Destination directory.</param>
    /// <param name="fileName">Requested file name.</param>
    /// <returns>A path that does not exist yet.</returns>
    private static string GetUniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);

        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var i = 1; i < 10_000; i++)
        {
            path = Path.Combine(
                directory,
                stem + "-" + i.ToString(CultureInfo.InvariantCulture) + extension);

            if (!File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(directory, stem + "-" + Guid.NewGuid().ToString("N") + extension);
    }

    /// <summary>Writes a small text response.</summary>
    /// <param name="stream">Client stream.</param>
    /// <param name="status">HTTP status code.</param>
    /// <param name="reason">HTTP reason phrase.</param>
    /// <param name="headOnly">True to omit the body.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task that completes after the response is written.</returns>
    private static Task WriteStatusAsync(
        NetworkStream stream,
        int status,
        string reason,
        bool headOnly,
        CancellationToken cancellation)
    {
        var body = Encoding.UTF8.GetBytes(status.ToString(CultureInfo.InvariantCulture) + " " + reason + "\n");

        return WriteBodyAsync(
            stream,
            status,
            reason,
            "text/plain; charset=utf-8",
            body,
            extraHeaders: null,
            headOnly,
            cancellation);
    }

    /// <summary>Writes a buffered response body.</summary>
    /// <param name="stream">Client stream.</param>
    /// <param name="status">HTTP status code.</param>
    /// <param name="reason">HTTP reason phrase.</param>
    /// <param name="contentType">Response media type.</param>
    /// <param name="body">Response body bytes.</param>
    /// <param name="extraHeaders">Optional additional header lines.</param>
    /// <param name="headOnly">True to omit the body.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task that completes after the response is written.</returns>
    private static async Task WriteBodyAsync(
        NetworkStream stream,
        int status,
        string reason,
        string contentType,
        byte[] body,
        string? extraHeaders,
        bool headOnly,
        CancellationToken cancellation)
    {
        var header = "HTTP/1.1 " + status.ToString(CultureInfo.InvariantCulture) + " " + reason + "\r\n" +
            "Content-Type: " + contentType + "\r\n" +
            "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n" +
            (extraHeaders ?? string.Empty) +
            "\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellation).ConfigureAwait(false);

        if (!headOnly)
        {
            await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    /// <summary>Writes a file response without loading it into memory.</summary>
    /// <param name="stream">Client stream.</param>
    /// <param name="file">File to send.</param>
    /// <param name="extraHeaders">Additional header lines.</param>
    /// <param name="headOnly">True to omit the body.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>A task that completes after the response is written.</returns>
    private static async Task WriteFileAsync(
        NetworkStream stream,
        FileInfo file,
        string extraHeaders,
        bool headOnly,
        CancellationToken cancellation)
    {
        var header = "HTTP/1.1 200 OK\r\n" +
            "Content-Type: " + ContentTypeFor(file.Name) + "\r\n" +
            "Content-Length: " + file.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n" +
            extraHeaders +
            "\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellation).ConfigureAwait(false);

        if (headOnly)
        {
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
            return;
        }

        await using var source = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            useAsync: true);

        await source.CopyToAsync(stream, 64 * 1024, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    /// <summary>Builds the HTML index for one directory under the served root.</summary>
    /// <param name="root">The served directory.</param>
    /// <param name="directory">The directory to list.</param>
    /// <returns>An HTML document.</returns>
    /// <remarks>
    /// The root page highlights recognizable build artifacts found anywhere below
    /// it, then lists the immediate folders so large desktop output trees (VLC
    /// plugins, for example) can be browsed without dumping thousands of rows
    /// onto one page.
    /// </remarks>
    private string BuildIndex(string root, string directory)
    {
        var relativeDirectory = Path.GetRelativePath(root, directory).Replace('\\', '/');

        if (relativeDirectory == ".")
        {
            relativeDirectory = string.Empty;
        }

        var builder = new StringBuilder(4096);
        builder.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.Append("<title>Donkeh downloads</title><style>");
        builder.Append("body{font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;background:#0f172a;color:#e2e8f0;margin:0;padding:32px;}");
        builder.Append("main{max-width:980px;margin:0 auto;}");
        builder.Append("h1{font-size:28px;margin:0 0 6px;}");
        builder.Append("h2{font-size:16px;letter-spacing:0.5px;color:#9fb0c8;margin:24px 0 10px;}");
        builder.Append("p{color:#9fb0c8;margin:0 0 20px;line-height:1.5;}");
        builder.Append("code{background:#172033;border:1px solid #273449;border-radius:5px;padding:2px 6px;font-size:13px;}");
        builder.Append("ul{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:8px;}");
        builder.Append("li{background:#172033;border:1px solid #273449;border-radius:10px;padding:12px 14px;display:flex;justify-content:space-between;gap:16px;align-items:center;}");
        builder.Append("a{color:#93c5fd;text-decoration:none;word-break:break-all;}");
        builder.Append("a:hover{text-decoration:underline;}");
        builder.Append(".size{color:#8fa0b8;font-size:12px;white-space:nowrap;}");
        builder.Append(".upload{border:2px dashed #3b4a63;border-radius:12px;background:#141d2d;padding:18px;text-align:center;cursor:pointer;margin:18px 0 8px;transition:border-color .15s,background .15s;}");
        builder.Append(".upload.over{border-color:#93c5fd;background:#1b2941;}");
        builder.Append(".upload-title{font-size:15px;font-weight:600;color:#e2e8f0;}");
        builder.Append(".upload-sub{color:#8fa0b8;font-size:13px;margin-top:4px;}");
        builder.Append(".upload-status{font-size:12px;margin-top:8px;min-height:16px;color:#8fa0b8;white-space:pre-wrap;word-break:break-all;}");
        builder.Append(".upload-status.ok{color:#81c995;}");
        builder.Append(".upload-status.fail{color:#f28b82;}");
        builder.Append(".upload-status.busy{color:#f0b26b;}");
        builder.Append("#file{display:none;}");
        builder.Append("</style></head><body><main><h1>Donkeh builds</h1><p>Serving <code>");
        builder.Append(WebUtility.HtmlEncode(root));
        builder.Append("</code></p>");

        if (relativeDirectory.Length > 0)
        {
            builder.Append("<p>Folder <code>");
            builder.Append(WebUtility.HtmlEncode(relativeDirectory));
            builder.Append("</code></p>");
        }

        builder.Append("<div class=\"upload\" id=\"drop\">");
        builder.Append("<input id=\"file\" type=\"file\" accept=\"image/*\" multiple>");
        builder.Append("<div class=\"upload-title\">Drop screenshots here</div>");
        builder.Append("<div class=\"upload-sub\">or click to browse &middot; saved to <code>");

        if (string.IsNullOrWhiteSpace(_uploadDirectory))
        {
            builder.Append("uploads are disabled");
        }
        else
        {
            var uploadLabel = _uploadDirectory;

            try
            {
                var uploadRoot = Path.GetFullPath(_uploadDirectory);
                uploadLabel = Path.GetRelativePath(
                    Path.GetFullPath(root),
                    uploadRoot).Replace('\\', '/');

                if (uploadLabel.StartsWith("..", StringComparison.Ordinal))
                {
                    uploadLabel = uploadRoot;
                }
            }
            catch (Exception)
            {
                // Keep the configured text if the path cannot be normalised.
            }

            builder.Append(WebUtility.HtmlEncode(uploadLabel));
        }

        builder.Append("</code></div>");
        builder.Append("<div class=\"upload-status\" id=\"status\"></div>");
        builder.Append("</div>");

        if (relativeDirectory.Length == 0)
        {
            var artifacts = FindArtifacts(root);

            if (artifacts.Count > 0)
            {
                builder.Append("<h2>ARTIFACTS</h2><ul>");

                foreach (var artifact in artifacts)
                {
                    var relative = Path.GetRelativePath(root, artifact.FullName).Replace('\\', '/');
                    AppendEntry(builder, EncodeHref(relative), relative, FormatSize(artifact.Length));
                }

                builder.Append("</ul>");
            }
        }

        var directories = new List<string>();
        var files = new List<FileInfo>();

        try
        {
            directories.AddRange(Directory.GetDirectories(directory));
            files.AddRange(new DirectoryInfo(directory).GetFiles());
        }
        catch (Exception)
        {
            // An unreadable directory produces an empty listing.
        }

        directories.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort((left, right) => string.Compare(
            left.Name,
            right.Name,
            StringComparison.OrdinalIgnoreCase));

        builder.Append(relativeDirectory.Length == 0 ? "<h2>FOLDERS</h2><ul>" : "<h2>CONTENTS</h2><ul>");

        if (relativeDirectory.Length > 0)
        {
            var slash = relativeDirectory.LastIndexOf('/');
            var parent = slash < 0 ? string.Empty : relativeDirectory[..slash];
            AppendEntry(builder, EncodeHref(parent), "..", size: null);
        }

        foreach (var child in directories)
        {
            var relative = Path.GetRelativePath(root, child).Replace('\\', '/');
            AppendEntry(builder, EncodeHref(relative) + "/", Path.GetFileName(child) + "/", size: null);
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
            AppendEntry(builder, EncodeHref(relative), file.Name, FormatSize(file.Length));
        }

        builder.Append("</ul>");

        if (relativeDirectory.Length == 0 && directories.Count == 0 && files.Count == 0)
        {
            builder.Append("<p>No builds yet. Use the Deploy tab to build a target.</p>");
        }

        builder.Append(
            """
            <script>
            (function () {
              var drop = document.getElementById('drop');
              var input = document.getElementById('file');
              var status = document.getElementById('status');

              function setStatus(text, kind) {
                status.textContent = text;
                status.className = 'upload-status' + (kind ? ' ' + kind : '');
              }

              async function upload(file) {
                setStatus('Uploading ' + file.name + ' ...', 'busy');

                try {
                  var response = await fetch('/upload?name=' + encodeURIComponent(file.name), {
                    method: 'POST',
                    headers: { 'Content-Type': file.type || 'application/octet-stream' },
                    body: file
                  });

                  var data = null;

                  try {
                    data = await response.json();
                  } catch (ignored) {
                  }

                  if (!response.ok || !data || !data.ok) {
                    throw new Error((data && data.error) || ('HTTP ' + response.status));
                  }

                  setStatus('Saved ' + data.path, 'ok');
                } catch (error) {
                  setStatus('Upload failed: ' + error.message, 'fail');
                }
              }

              function uploadAll(files) {
                for (var i = 0; i < files.length; i++) {
                  var file = files[i];

                  if (file.type.indexOf('image/') === 0
                      || /\.(png|jpe?g|gif|webp|bmp|svg|avif|tiff?)$/i.test(file.name)) {
                    upload(file);
                  } else {
                    setStatus('Skipped ' + file.name + ' (not an image).', 'fail');
                  }
                }
              }

              drop.addEventListener('click', function (event) {
                if (event.target !== input) {
                  input.click();
                }
              });

              drop.addEventListener('dragover', function (event) {
                event.preventDefault();
                drop.classList.add('over');
              });

              drop.addEventListener('dragleave', function () {
                drop.classList.remove('over');
              });

              drop.addEventListener('drop', function (event) {
                event.preventDefault();
                drop.classList.remove('over');

                if (event.dataTransfer) {
                  uploadAll(event.dataTransfer.files);
                }
              });

              input.addEventListener('change', function () {
                if (input.files) {
                  uploadAll(input.files);
                }

                input.value = '';
              });

              window.addEventListener('dragover', function (event) {
                event.preventDefault();
              });

              window.addEventListener('drop', function (event) {
                event.preventDefault();
              });
            })();
            </script>
            """);
        builder.Append("</main></body></html>");
        return builder.ToString();
    }

    /// <summary>Appends one linked directory or file row.</summary>
    /// <param name="builder">Destination document builder.</param>
    /// <param name="href">Link target.</param>
    /// <param name="label">Visible label.</param>
    /// <param name="size">Optional formatted size.</param>
    private static void AppendEntry(StringBuilder builder, string href, string label, string? size)
    {
        builder.Append("<li><a href=\"");
        builder.Append(WebUtility.HtmlEncode(href));
        builder.Append("\">");
        builder.Append(WebUtility.HtmlEncode(label));
        builder.Append("</a>");

        if (size is not null)
        {
            builder.Append("<span class=\"size\">");
            builder.Append(size);
            builder.Append("</span>");
        }

        builder.Append("</li>");
    }

    /// <summary>Escapes a root-relative path for use in a URL.</summary>
    /// <param name="relative">Slash-separated relative path; empty for the root.</param>
    /// <returns>An absolute URL path beginning with <c>/</c>.</returns>
    private static string EncodeHref(string relative)
    {
        if (relative.Length == 0)
        {
            return "/";
        }

        return "/" + string.Join(
            "/",
            relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
    }

    /// <summary>Finds recognizable build artifacts below the root.</summary>
    /// <param name="root">The served directory.</param>
    /// <returns>At most 100 artifacts, sorted by path.</returns>
    private static List<FileInfo> FindArtifacts(string root)
    {
        var results = new List<FileInfo>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!IsArtifact(path))
                {
                    continue;
                }

                results.Add(new FileInfo(path));

                if (results.Count >= 100)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // Keep whatever was found before the read failed.
        }

        results.Sort((left, right) => string.Compare(
            left.FullName,
            right.FullName,
            StringComparison.OrdinalIgnoreCase));

        return results;
    }

    /// <summary>Returns whether a file looks like a downloadable build artifact.</summary>
    /// <param name="path">File path.</param>
    /// <returns><see langword="true"/> for installers, packages and archives.</returns>
    private static bool IsArtifact(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".apk" or ".aab" or ".exe" or ".msi" or ".zip" or ".7z" or ".nupkg"
            or ".app" or ".dmg" or ".deb" or ".rpm" or ".tar" or ".gz" => true,
        _ => false
    };

    /// <summary>Formats a byte count for the index.</summary>
    /// <param name="bytes">File length.</param>
    /// <returns>A short size string.</returns>
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>Returns a media type for a downloaded file.</summary>
    /// <param name="fileName">File name.</param>
    /// <returns>A content type.</returns>
    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".apk" => "application/vnd.android.package-archive",
        ".zip" => "application/zip",
        ".json" => "application/json",
        ".xml" => "text/xml; charset=utf-8",
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".txt" or ".log" => "text/plain; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".avif" => "image/avif",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream"
    };

    /// <summary>Escapes a filename for a Content-Disposition header.</summary>
    /// <param name="name">File name.</param>
    /// <returns>The escaped name.</returns>
    private static string EscapeHeader(string name) =>
        name.Replace("\\", "_", StringComparison.Ordinal)
            .Replace("\"", "'", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

    /// <summary>Determines whether a full path stays below the served root.</summary>
    /// <param name="root">The served root.</param>
    /// <param name="fullPath">The resolved candidate path.</param>
    /// <returns><see langword="true"/> when the path is inside the root.</returns>
    private static bool IsUnderRoot(string root, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(fullPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedRoot, comparison))
        {
            return true;
        }

        return normalizedPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            comparison);
    }
}
