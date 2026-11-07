using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Ignite.Parser.Compilation
{
    public class ILGeneratorWrapper
    {
        /// <summary>
        /// IL dumping is opt-in because it dominates compilation time and floods
        /// the console. Set IGNITE_SHOW_IL=1 (or true) to enable it.
        /// </summary>
        public static readonly bool SHOW_GENERATED_IL =
            Environment.GetEnvironmentVariable("IGNITE_SHOW_IL") is "1" or "true" or "TRUE" or "True";

        private static readonly Type[] DefaultParameterTypes = { typeof(string), typeof(int) };

        private readonly DynamicMethod _method;
        private readonly ILGenerator _generator;

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

        public int ILOffset => _generator.ILOffset;

        // ------------------------------------------------------------------
        // Formatting helpers (no LINQ, only reached when SHOW_GENERATED_IL)
        // ------------------------------------------------------------------

        private static string FormatType(Type? type) => type == null ? "<null>" : type.FullName ?? type.Name;

        private static string FormatTypes(Type[]? types)
        {
            if (types == null || types.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < types.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatType(types[i]));
            }
            return sb.ToString();
        }

        private static string FormatLabel(Label label) => "L" + label.GetHashCode();

        private static string FormatLabels(Label[] labels)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatLabel(labels[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string FormatMethod(MethodBase method)
        {
            var sb = new StringBuilder();
            sb.Append(FormatType(method.DeclaringType)).Append("::").Append(method.Name).Append('(');

            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatType(parameters[i].ParameterType));
            }
            sb.Append(')');
            return sb.ToString();
        }

        private void Log(OpCode opcode)
        {
            Console.WriteLine("IL_" + _generator.ILOffset.ToString("X4") + ": " + opcode.Name);
        }

        private void Log(OpCode opcode, string operand)
        {
            Console.WriteLine("IL_" + _generator.ILOffset.ToString("X4") + ": " + (opcode.Name ?? string.Empty).PadRight(12) + " " + operand);
        }

        private void LogRaw(string message)
        {
            Console.WriteLine("IL_" + _generator.ILOffset.ToString("X4") + ": " + message);
        }

        // ------------------------------------------------------------------
        // Emit overloads
        // ------------------------------------------------------------------

        public void Emit(OpCode opcode)
        {
            if (SHOW_GENERATED_IL) Log(opcode);
            _generator.Emit(opcode);
        }

        public void Emit(OpCode opcode, byte arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, sbyte arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, short arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, int arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, long arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString());
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, float arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, double arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, arg.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, string arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, "\"" + arg + "\"");
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, Type arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatType(arg));
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, FieldInfo arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatType(arg.DeclaringType) + "::" + arg.Name);
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, MethodInfo arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatMethod(arg));
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, ConstructorInfo arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatMethod(arg));
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, Label arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatLabel(arg));
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, Label[] arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, FormatLabels(arg));
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, LocalBuilder arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, "V_" + arg.LocalIndex + " (" + FormatType(arg.LocalType) + ")");
            _generator.Emit(opcode, arg);
        }

        public void Emit(OpCode opcode, SignatureHelper arg)
        {
            if (SHOW_GENERATED_IL) Log(opcode, "<signature>");
            _generator.Emit(opcode, arg);
        }

        // ------------------------------------------------------------------
        // Calls
        // ------------------------------------------------------------------

        public void EmitCall(OpCode opcode, MethodInfo method, Type[]? optionalParameterTypes = null)
        {
            if (SHOW_GENERATED_IL)
            {
                string extra = optionalParameterTypes == null || optionalParameterTypes.Length == 0
                    ? string.Empty
                    : " varargs(" + FormatTypes(optionalParameterTypes) + ")";
                Log(opcode, FormatMethod(method) + extra);
            }
            _generator.EmitCall(opcode, method, optionalParameterTypes);
        }

        public void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType,
            Type[]? parameterTypes, Type[]? optionalParameterTypes)
        {
            if (SHOW_GENERATED_IL)
            {
                Log(opcode, FormatType(returnType) + "(" + FormatTypes(parameterTypes) + ")");
            }
            _generator.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
        }

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

        public Label DefineLabel()
        {
            Label label = _generator.DefineLabel();
            if (SHOW_GENERATED_IL) Console.WriteLine("// define " + FormatLabel(label));
            return label;
        }

        public void MarkLabel(Label label)
        {
            if (SHOW_GENERATED_IL) Console.WriteLine(FormatLabel(label) + ": (IL_" + _generator.ILOffset.ToString("X4") + ")");
            _generator.MarkLabel(label);
        }

        public LocalBuilder DeclareLocal(Type type)
        {
            LocalBuilder local = _generator.DeclareLocal(type);
            if (SHOW_GENERATED_IL) Console.WriteLine("// .local V_" + local.LocalIndex + " : " + FormatType(type));
            return local;
        }

        public LocalBuilder DeclareLocal(Type type, bool pinned)
        {
            LocalBuilder local = _generator.DeclareLocal(type, pinned);
            if (SHOW_GENERATED_IL)
            {
                Console.WriteLine("// .local V_" + local.LocalIndex + " : " + FormatType(type) + (pinned ? " (pinned)" : ""));
            }
            return local;
        }

        // ------------------------------------------------------------------
        // Exception handling
        // ------------------------------------------------------------------

        public Label BeginExceptionBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw(".try {");
            return _generator.BeginExceptionBlock();
        }

        public void BeginCatchBlock(Type? exceptionType)
        {
            if (SHOW_GENERATED_IL) LogRaw("} catch " + FormatType(exceptionType) + " {");
            _generator.BeginCatchBlock(exceptionType);
        }

        public void BeginExceptFilterBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("} filter {");
            _generator.BeginExceptFilterBlock();
        }

        public void BeginFaultBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("} fault {");
            _generator.BeginFaultBlock();
        }

        public void BeginFinallyBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("} finally {");
            _generator.BeginFinallyBlock();
        }

        public void EndExceptionBlock()
        {
            if (SHOW_GENERATED_IL) LogRaw("}");
            _generator.EndExceptionBlock();
        }

        public void ThrowException(Type excType)
        {
            if (SHOW_GENERATED_IL) LogRaw("// throw new " + FormatType(excType) + "()");
            _generator.ThrowException(excType);
        }

        // ------------------------------------------------------------------
        // Convenience
        // ------------------------------------------------------------------

        public void EmitWriteLine(string value)
        {
            if (SHOW_GENERATED_IL) LogRaw("// WriteLine \"" + value + "\"");
            _generator.EmitWriteLine(value);
        }

        public void EmitWriteLine(LocalBuilder local)
        {
            if (SHOW_GENERATED_IL) LogRaw("// WriteLine V_" + local.LocalIndex);
            _generator.EmitWriteLine(local);
        }

        // ------------------------------------------------------------------
        // Finalisation
        // ------------------------------------------------------------------

        public TDelegate Compile<TDelegate>() where TDelegate : Delegate
        {
            if (SHOW_GENERATED_IL) Console.WriteLine("// end method (" + _generator.ILOffset + " bytes)");
            return (TDelegate)_method.CreateDelegate(typeof(TDelegate));
        }
    }
}