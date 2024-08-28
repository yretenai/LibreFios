namespace LibreFios.Structures;

[Flags]
public enum PSARCArchiveFlags : uint {
	CaseInsensitivePaths = 1 << 0,
	AbsolutePaths = 1 << 1,
	EncryptedFiles = 1 << 2,
}

public static class PSARCArchiveFlagsExtensions {
	public static bool HasFlagFast(this PSARCArchiveFlags value, PSARCArchiveFlags ArchiveFlags) => (value & ArchiveFlags) != 0;
}
