using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read PE metadata only. Does not load or execute WPF or any NAP dependency.
static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
var binary = args.Length == 1 ? args[0] : null;
Require(File.Exists(binary), "Provide the cross-built NAP.GitVisualLab.dll path.");
using var stream = File.OpenRead(binary!);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var ops = typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
    .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
var calls = new List<string>();
foreach (var handle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(handle);
    var typeName = metadata.GetString(type.Name);
    if (!typeName.StartsWith("<OnStartup>", StringComparison.Ordinal) && typeName != "Phase81GitLab") continue;
    foreach (var mh in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(mh);
        if (metadata.GetString(method.Name) is not ("MoveNext" or "Start")) continue;
        var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        for (var p = 0; p < il.Length;)
        {
            ushort code = il[p++];
            if (code == 0xfe) code = (ushort)(0xfe00 | il[p++]);
            var op = ops[code];
            if (op.OperandType == OperandType.InlineMethod)
            {
                var target = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(BitConverter.ToInt32(il, p));
                if (target.Kind == HandleKind.MemberReference)
                {
                    var member = metadata.GetMemberReference((MemberReferenceHandle)target);
                    var parent = member.Parent;
                    var name = parent.Kind == HandleKind.TypeReference ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name) : parent.Kind.ToString();
                    calls.Add(name + "." + metadata.GetString(member.Name));
                }
                if (target.Kind == HandleKind.MethodDefinition)
                {
                    var member = metadata.GetMethodDefinition((MethodDefinitionHandle)target);
                    calls.Add(metadata.GetString(metadata.GetTypeDefinition(member.GetDeclaringType()).Name) + "." + metadata.GetString(member.Name));
                }
            }
            p += op.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, p),
                _ => 4 };
        }
    }
}
Require(calls.Contains("Phase81GitLab.Start"), "Missing dedicated startup call.");
Require(calls.Contains("GitVisualLabContract.Validate"), "Missing isolation validation call.");
foreach (var forbidden in new[] { "ProfessionalUiService", "ExplorerViewModel", "ShellViewModel", "MainWindow", "UniverseProfileStore",
    "AutomationHostClient", "RemoteDesktopService", "LicenseRegister", "GitProductionService", "ExecutionMutex", "AutomationPipeServer", "HttpClient", "Process" })
    Require(!calls.Any(c => c.StartsWith(forbidden + ".", StringComparison.Ordinal)), "Forbidden composition dependency: " + forbidden);
Console.WriteLine("F81 compiled startup IL: PASS (dedicated composition, no productive startup dependencies)");
