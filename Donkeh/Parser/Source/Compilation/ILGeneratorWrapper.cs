using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Donkeh.Parser.Compilation
{
    /// <summary>
    /// Thin wrapper over <see cref="ILGenerator"/> that adds readable IL
    /// logging and compiles the emitted instructions into a delegate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each instance owns one <see cref="DynamicMethod"/> that is created up
    /// front. <see cref="Compile{TDelegate}"/> can be called once; the instance
    /// is not reusable afterwards and is not thread-safe.
    /// </para>
    /// <para>
    /// When <see cref="SHOW_GENERATED_IL"/> is enabled, every emit writes a
    /// human-readable line to <see cref="Console"/>. Logging happens before the
    /// instruction is emitted and uses the current IL offset.
    /// </para>
    /// </remarks>
    /// <seealso cref="Compiler"/>
    public class ILGeneratorWrapper
    {
        /// <summary>
        /// IL dumping is opt-in because it dominates compilation time and floods
        /// the console. Set IGNITE_SHOW_IL=1 (or true) to enable it.
        /// </summary>
        /// <remarks>
        /// The environment variable is read once, when the type is initialised.
        /// Accepted values are <c>"1"</c>, <c>"true"</c>, <c>"TRUE"</c> and
        /// <c>"True"</c>; any other value disables logging.
        /// </remarks>
        public static readonly bool SHOW_GENERATED_IL =
            Environment.GetEnvironmentVariable("IGNITE_SHOW_IL") is "1" or "true" or "TRUE" or "True";

        /// <summary>
        /// Parameter types used by the parameterless constructor: a string and
        /// an integer, matching the historical tokenizer signature.
        /// </summary>
        private static readonly Type[] DefaultParameterTypes = { typeof(string), typeof(int) };

        /// <summary>
        /// The dynamic method that receives the emitted instructions.
        /// </summary>
        private readonly DynamicMethod _method;

        /// <summary>
        /// The underlying generator owned by <see cref="_method"/>.
        /// </summary>
        private readonly ILGenerator _generator;

        /// <summary>
        /// Creates a wrapper around a new dynamic method.
        /// </summary>
        /// <param name="name">
        /// The diagnostic name of the generated method. Defaults to
        /// <c>"tokeniser"</c>.
        /// </param>
        /// <param name="returnType">
        /// The method's return type, or <see langword="null"/> for
        /// <see cref="int"/>.
        /// </param>
        /// <param name="parameterTypes">
        /// The method's parameter types, or <see langword="null"/> for
        /// <see cref="DefaultParameterTypes"/>.
        /// </param>
        /// <remarks>
        /// The method is associated with this assembly's module and created with
        /// <c>skipVisibility: true</c> so generated code can access non-public
        /// members.
        /// </remarks>
        public ILGeneratorWrapper(
            string name = "tokeniser",
            Type? returnType = null,
            Type[]? parameterTypes = null)
        {
            returnType ??= typeof(int);
            parameterTypes ??= DefaultParameterTypes;

            _method = new DynamicMethod(
                name: name,
                returnType: returnType,
                parameterTypes: parameterTypes,
                m: typeof(ILGeneratorWrapper).Module,
                skipVisibility: true);

            _generator = _method.GetILGenerator();

            if (SHOW_GENERATED_IL)
            {
                Console.WriteLine("// method " + name + "(" + FormatTypes(parameterTypes) + ") : " + FormatType(returnType));
            }
        }

        /// <summary>
        /// Gets the current offset, in bytes, into the method body being emitted.
        /// </summary>
        /// <value>The number of bytes emitted so far.</value>
        public int ILOffset => _generator.ILOffset;

        // ------------------------------------------------------------------
        // Formatting helpers (no LINQ, only reached when SHOW_GENERATED_IL)
        // ------------------------------------------------------------------

        /// <summary>
        /// Formats a type for IL logging, preferring the full name and falling
        /// back to the short name for generic parameters.
        /// </summary>
        /// <param name="type">The type to format, or <see langword="null"/>.</param>
        /// <returns>The formatted type name, or <c>"&lt;null&gt;"</c>.</returns>
        private static string FormatType(Type? type) => type == null ? "<null>" : type.FullName ?? type.Name;

        /// <summary>
        /// Formats a parameter-type list as a comma-separated string.
        /// </summary>
        /// <param name="types">The types to format, or <see langword="null"/>.</param>
        /// <returns>An empty string for a null or empty array.</returns>
        private static string FormatTypes(Type[]? types)
        {
            if (types == null || types.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            for (var i = 0; i < types.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatType(types[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Formats a label as <c>L</c> followed by its hash code, matching the
        /// style used in IL dumps.
        /// </summary>
        /// <param name="label">The label to format.</param>
        /// <returns>The formatted label.</returns>
        private static string FormatLabel(Label label) => "L" + label.GetHashCode();

        /// <summary>
        /// Formats a label array for a switch instruction.
        /// </summary>
        /// <param name="labels">The labels to format.</param>
        /// <returns>A comma-separated list enclosed in square brackets.</returns>
        private static string FormatLabels(Label[] labels)
        {
            var sb = new StringBuilder("[");
            for (var i = 0; i < labels.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatLabel(labels[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// Formats a method as <c>DeclaringType::Name(paramTypes)</c>.
        /// </summary>
        /// <param name="method">The method or constructor to format.</param>
        /// <returns>The formatted signature.</returns>
        private static string FormatMethod(MethodBase method)
        {
            var sb = new StringBuilder();
            sb.Append(FormatType(method.DeclaringType)).Append("::").Append(method.Name).Append('(');

            var parameters = method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatType(parameters[i].ParameterType));
            }
            sb.Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Writes an opcode with no operand to the IL dump.
        /// </summary>
        /// <param name="opcode">The opcode that is about to be emitted.</param>
        private void Log(OpCode opcode)
        {
            Console.WriteLine("IL_" + _generator.ILOffset.ToString("X4") + ": " + opcode.Name);
        }

        /// <summary>
        /// Writes an opcode and a pre-formatted operand to the IL dump.
        /// </summary>
        /// <param name="opcode">The opcode that is about to be emitted.</param>
        /// <param name="operand">The formatted operand text.</param>
        private void Log(OpCode opcode, string operand)
        {
            Console.WriteLine("IL_" + _generator.ILOffset.ToString("X4") + ": " + (opcode.Name ?? string.Empty).PadRight(12) + " " + operand);
        }

        /// <summary>
        /// Writes an arbitrary diagnostic line prefixed with the current IL
        /// offset.
        /// </summary>
        /// <param name="message">The message to write.</param>
        private void LogRaw(string message)
        {
            Console.WriteLine("IL_" + _generator.ILOffset.ToString("X4") + ": " + message);
        }

        // ------------------------------------------------------------------
        // Emit overloads
        // ------------------------------------------------------------------

        /// <summary>Emits an opcode that takes no operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        public void Emit(OpCode opcode)
        {
            if (SHOW_GENERATED_IL) Log(opcode);
            _generator.Emit(opcode);
        }

        /// <summary>Emits an opcode with a <see cref="byte"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand.</param>
        public void Emit(OpCode opcode, byte arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with an <see cref="sbyte"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand.</param>
        public void Emit(OpCode opcode, sbyte arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a <see cref="short"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand.</param>
        public void Emit(OpCode opcode, short arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with an <see cref="int"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand.</param>
        public void Emit(OpCode opcode, int arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a <see cref="long"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand.</param>
        public void Emit(OpCode opcode, long arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        /// <summary>
        /// Emits an opcode with a <see cref="float"/> operand.
        /// </summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand, formatted invariantly in the IL dump.</param>
        public void Emit(OpCode opcode, float arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _generator.Emit(opcode, arg);
        }

        /// <summary>
        /// Emits an opcode with a <see cref="double"/> operand.
        /// </summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand, formatted invariantly in the IL dump.</param>
        public void Emit(OpCode opcode, double arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a string operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand, quoted in the IL dump.</param>
        public void Emit(OpCode opcode, string arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, "\"" + arg + "\"");
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a <see cref="Type"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The operand.</param>
        public void Emit(OpCode opcode, Type arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatType(arg));
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a <see cref="FieldInfo"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The field operand.</param>
        public void Emit(OpCode opcode, FieldInfo arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatType(arg.DeclaringType) + "::" + arg.Name);
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a <see cref="MethodInfo"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The method operand.</param>
        public void Emit(OpCode opcode, MethodInfo arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatMethod(arg));
            _generator.Emit(opcode, arg);
        }

        /// <summary>
        /// Emits an opcode with a <see cref="ConstructorInfo"/> operand.
        /// </summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The constructor operand.</param>
        public void Emit(OpCode opcode, ConstructorInfo arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatMethod(arg));
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a single <see cref="Label"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The branch target.</param>
        public void Emit(OpCode opcode, Label arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatLabel(arg));
            _generator.Emit(opcode, arg);
        }

        /// <summary>
        /// Emits a switch opcode with its table of <see cref="Label"/> targets.
        /// </summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The switch targets, in case order.</param>
        public void Emit(OpCode opcode, Label[] arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatLabels(arg));
            _generator.Emit(opcode, arg);
        }

        /// <summary>Emits an opcode with a <see cref="LocalBuilder"/> operand.</summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The local variable operand.</param>
        public void Emit(OpCode opcode, LocalBuilder arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, "V_" + arg.LocalIndex + " (" + FormatType(arg.LocalType) + ")");
            _generator.Emit(opcode, arg);
        }

        /// <summary>
        /// Emits an opcode with a <see cref="SignatureHelper"/> operand, used
        /// for call-site signatures.
        /// </summary>
        /// <param name="opcode">The opcode to emit.</param>
        /// <param name="arg">The signature operand.</param>
        public void Emit(OpCode opcode, SignatureHelper arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, "<signature>");
            _generator.Emit(opcode, arg);
        }

        // ------------------------------------------------------------------
        // Calls
        // ------------------------------------------------------------------

        /// <summary>
        /// Emits a call instruction, optionally with a varargs signature.
        /// </summary>
        /// <param name="opcode">The call opcode, such as <see cref="OpCodes.Call"/>.</param>
        /// <param name="method">The method to call.</param>
        /// <param name="optionalParameterTypes">
        /// The varargs parameter types, or <see langword="null"/> for a normal
        /// call.
        /// </param>
        public void EmitCall(OpCode opcode, MethodInfo method, Type[]? optionalParameterTypes = null)
        {
            if (SHOW_GENERATED_IL)
            {
                var extra = optionalParameterTypes == null || optionalParameterTypes.Length == 0
                    ? string.Empty
                    : " varargs(" + FormatTypes(optionalParameterTypes) + ")";
                Log(opcode, FormatMethod(method) + extra);
            }
            _generator.EmitCall(opcode, method, optionalParameterTypes);
        }

        /// <summary>
        /// Emits an indirect call using a managed calling convention.
        /// </summary>
        /// <param name="opcode">The <c>calli</c> opcode.</param>
        /// <param name="callingConvention">The call-site calling convention.</param>
        /// <param name="returnType">The call-site return type.</param>
        /// <param name="parameterTypes">The required parameter types.</param>
        /// <param name="optionalParameterTypes">
        /// The varargs parameter types, or <see langword="null"/>.
        /// </param>
        public void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType,
            Type[]? parameterTypes, Type[]? optionalParameterTypes)
        {
            if (SHOW_GENERATED_IL)
            {
                Log(opcode, FormatType(returnType) + "(" + FormatTypes(parameterTypes) + ")");
            }
            _generator.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
        }

        /// <summary>
        /// Emits an indirect call using an unmanaged calling convention.
        /// </summary>
        /// <param name="opcode">The <c>calli</c> opcode.</param>
        /// <param name="unmanagedCallConv">The unmanaged calling convention.</param>
        /// <param name="returnType">The call-site return type.</param>
        /// <param name="parameterTypes">The parameter types.</param>
        public void EmitCalli(OpCode opcode, System.Runtime.InteropServices.CallingConvention unmanagedCallConv,
            Type? returnType, Type[]? parameterTypes)
        {
            if (SHOW_GENERATED_IL)
            {
                Log(opcode, "unmanaged " + unmanagedCallConv + " " + FormatType(returnType) + "(" + FormatTypes(parameterTypes) + ")");
            }
            _generator.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
        }

        // ------------------------------------------------------------------
        // Labels and locals
        // ------------------------------------------------------------------

        /// <summary>
        /// Defines a new branch label in the method being emitted.
        /// </summary>
        /// <returns>The new label.</returns>
        public Label DefineLabel()
        {
            var label = _generator.DefineLabel();
            if (SHOW_GENERATED_IL) Console.WriteLine("// define " + FormatLabel(label));
            return label;
        }

        /// <summary>
        /// Marks the current position as the target of <paramref name="label"/>.
        /// </summary>
        /// <param name="label">A label returned by <see cref="DefineLabel"/>.</param>
        public void MarkLabel(Label label)
        {
            if (SHOW_GENERATED_IL) Console.WriteLine(FormatLabel(label) + ": (IL_" + _generator.ILOffset.ToString("X4") + ")");
            _generator.MarkLabel(label);
        }

        /// <summary>
        /// Declares a new local variable in the method being emitted.
        /// </summary>
        /// <param name="type">The local's type.</param>
        /// <returns>The declared local.</returns>
        public LocalBuilder DeclareLocal(Type type)
        {
            var local = _generator.DeclareLocal(type);
            if (SHOW_GENERATED_IL) Console.WriteLine("// .local V_" + local.LocalIndex + " : " + FormatType(type));
            return local;
        }

        /// <summary>
        /// Declares a new local variable, optionally pinning its storage so a
        /// managed pointer into it can be passed to unmanaged code.
        /// </summary>
        /// <param name="type">The local's type.</param>
        /// <param name="pinned">Whether the local should be pinned.</param>
        /// <returns>The declared local.</returns>
        public LocalBuilder DeclareLocal(Type type, bool pinned)
        {
            var local = _generator.DeclareLocal(type, pinned);
            if (SHOW_GENERATED_IL)
            {
                Console.WriteLine("// .local V_" + local.LocalIndex + " : " + FormatType(type) + (pinned ? " (pinned)" : ""));
            }
            return local;
        }

        // ------------------------------------------------------------------
        // Exception handling
        // ------------------------------------------------------------------

        /// <summary>
        /// Begins a protected (<c>try</c>) region.
        /// </summary>
        /// <returns>The label marking the end of the exception block.</returns>
        public Label BeginExceptionBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw(".try {");
            return _generator.BeginExceptionBlock();
        }

        /// <summary>
        /// Begins a catch handler inside the current exception block.
        /// </summary>
        /// <param name="exceptionType">
        /// The type of exception to catch, or <see langword="null"/> to catch
        /// all exceptions.
        /// </param>
        public void BeginCatchBlock(Type? exceptionType)
        {
            if (SHOW_GENERATED_IL) LogRaw("} catch " + FormatType(exceptionType) + " {");
            _generator.BeginCatchBlock(exceptionType);
        }

        /// <summary>
        /// Begins an exception filter block inside the current exception block.
        /// </summary>
        public void BeginExceptFilterBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("} filter {");
            _generator.BeginExceptFilterBlock();
        }

        /// <summary>
        /// Begins a fault handler inside the current exception block.
        /// </summary>
        public void BeginFaultBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("} fault {");
            _generator.BeginFaultBlock();
        }

        /// <summary>
        /// Begins a finally handler inside the current exception block.
        /// </summary>
        public void BeginFinallyBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("} finally {");
            _generator.BeginFinallyBlock();
        }

        /// <summary>
        /// Ends the current exception block.
        /// </summary>
        public void EndExceptionBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("}");
            _generator.EndExceptionBlock();
        }

        /// <summary>
        /// Emits an instruction that throws a new exception of the given type.
        /// </summary>
        /// <param name="excType">The exception type to instantiate and throw.</param>
        public void ThrowException(Type excType)
        {
            if (SHOW_GENERATED_IL) LogRaw("// throw new " + FormatType(excType) + "()");
            _generator.ThrowException(excType);
        }

        // ------------------------------------------------------------------
        // Convenience
        // ------------------------------------------------------------------

        /// <summary>
        /// Emits code that writes the given string to the console.
        /// </summary>
        /// <param name="value">The string to write at run time.</param>
        public void EmitWriteLine(string value)
        {
            if (SHOW_GENERATED_IL) LogRaw("// WriteLine \"" + value + "\"");
            _generator.EmitWriteLine(value);
        }

        /// <summary>
        /// Emits code that writes the value of a local to the console.
        /// </summary>
        /// <param name="local">The local whose value is written at run time.</param>
        public void EmitWriteLine(LocalBuilder local)
        {
            if (SHOW_GENERATED_IL) LogRaw("// WriteLine V_" + local.LocalIndex);
            _generator.EmitWriteLine(local);
        }

        // ------------------------------------------------------------------
        // Finalisation
        // ------------------------------------------------------------------

        /// <summary>
        /// Completes the dynamic method and binds it to a delegate of type
        /// <typeparamref name="TDelegate"/>.
        /// </summary>
        /// <typeparam name="TDelegate">
        /// The delegate type. Its signature must match the return and parameter
        /// types passed to the constructor.
        /// </typeparam>
        /// <returns>The compiled delegate.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown by <see cref="DynamicMethod.CreateDelegate(Type)"/> when the
        /// delegate signature does not match the generated method.
        /// </exception>
        /// <remarks>
        /// The method must end with a <c>ret</c> instruction on every path.
        /// Compiling an instance twice is not supported.
        /// </remarks>
        public TDelegate Compile<TDelegate>() where TDelegate : Delegate
        {
            if (SHOW_GENERATED_IL) Console.WriteLine("// end method (" + _generator.ILOffset + " bytes)");
            return (TDelegate)_method.CreateDelegate(typeof(TDelegate));
        }
    }
}