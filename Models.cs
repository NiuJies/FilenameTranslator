namespace FilenameTranslator;

public sealed class FileItem
{
    public bool Selected { get; set; } = true;
    public string FullPath { get; set; } = "";
    public string DirectoryPath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string BaseName { get; set; } = "";
    public string Extension { get; set; } = "";
    public string Translation { get; set; } = "";
    public string FinalName { get; set; } = "";
    public string Status { get; set; } = "待翻译";
}

public sealed class RenameRecord
{
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
}
