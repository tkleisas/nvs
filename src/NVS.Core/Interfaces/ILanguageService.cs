using NVS.Core.Enums;

namespace NVS.Core.Interfaces;

public interface ILanguageService
{
    Language DetectLanguage(string filePath);
    string GetLanguageId(Language language);
    Language GetLanguageFromId(string languageId);
    IReadOnlyList<string> GetFileExtensions(Language language);
    string? GetLanguageServer(Language language);
}
