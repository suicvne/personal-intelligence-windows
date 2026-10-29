using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Intel.Core;
namespace Intel.Platform;
public sealed class Credentials : ICredentialStore {
    // The endpoint is public metadata, not a secret. Avoid a needless Windows CNG
    // dependency here: Whisky Wine 7.7 crashes in .NET 10 SHA256.HashData.
    private static string Key(string endpoint) => "IntelPrototype/" + endpoint;
    public string? Read(string endpoint) => OperatingSystem.IsWindows() ? WindowsRead(Key(endpoint)) : OperatingSystem.IsMacOS() ? MacRead(Key(endpoint)) : throw new PlatformNotSupportedException("Secure storage is unavailable. Use this token for the session only.");
    public void Write(string endpoint, string token) {
        if (OperatingSystem.IsWindows()) WindowsWrite(Key(endpoint), token);
        else if (OperatingSystem.IsMacOS()) MacWrite(Key(endpoint), token);
        else throw new PlatformNotSupportedException("Secure storage is unavailable. Use this token for the session only.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    private static string? WindowsRead(string key) {
        if (!CredRead(key, 1, 0, out var pointer)) {
            var code = Marshal.GetLastWin32Error(); if (code == 1168) return null;
            throw new InvalidOperationException($"Credential Manager read failed ({code}). Use session-only mode.");
        }
        try { var value = Marshal.PtrToStructure<Credential>(pointer); return Marshal.PtrToStringUni(value.CredentialBlob, (int)value.CredentialBlobSize / 2); }
        finally { CredFree(pointer); }
    }
    private static void WindowsWrite(string key, string token) {
        var pointer = Marshal.StringToCoTaskMemUni(token);
        try {
            var credential = new Credential { Type = 1, TargetName = key, UserName = "intel", CredentialBlob = pointer, CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(token), Persist = 2 };
            if (!CredWrite(ref credential, 0)) throw new InvalidOperationException($"Credential Manager write failed ({Marshal.GetLastWin32Error()}). Use session-only mode.");
        } finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }
    }
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint length, out IntPtr data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint length, byte[] data, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr item);
    private static readonly byte[] Account = "intel"u8.ToArray();
    private static string? MacRead(string key) {
        var service = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)Account.Length, Account, out var count, out var data, out var item);
        if (status == -25300) return null;
        Check(status);
        try { return Marshal.PtrToStringUTF8(data, (int)count); }
        finally { SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
    }
    private static void MacWrite(string key, string token) {
        var service = Encoding.UTF8.GetBytes(key); var bytes = Encoding.UTF8.GetBytes(token);
        IntPtr item = IntPtr.Zero;
        try {
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)Account.Length, Account, out _, out var data, out item);
            if (status == 0) { SecKeychainItemFreeContent(IntPtr.Zero, data); Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes)); }
            else if (status == -25300) Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)Account.Length, Account, (uint)bytes.Length, bytes, out item));
            else Check(status);
        } finally { CryptographicOperations.ZeroMemory(bytes); if (item != IntPtr.Zero) CFRelease(item); }
    }
    private static void Check(int status) { if (status != 0) throw new InvalidOperationException($"Keychain operation failed ({status}). Use session-only mode."); }
}
