using System.Text;
using GaameTranslate.Core;

namespace GaameTranslate.Infrastructure;

public static class VocabularyCsv
{
    private static readonly string[] Header = ["Word", "Translation", "SourceLanguage", "TargetLanguage", "Context", "TranslatedContext", "Notes", "Tags"];
    private static readonly string[] Extended = [.. Header, "Lemma", "Phonetic", "PartOfSpeech", "Level", "Definition", "Synonyms", "Examples", "Explanation", "DictionarySource", "ScreenshotFile", "AudioFile"];
    public static string Export(IEnumerable<VocabularyEntry> entries)
    {
        static string Field(string value)
        {
            // Prevent spreadsheet formula execution on export; stripped on reimport.
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return string.Join(",", Extended) + "\r\n" + string.Join("\r\n", entries.Select(e => string.Join(",", new[] { e.Word, e.Translation, e.SourceLanguage, e.TargetLanguage, e.Context, e.TranslatedContext, e.Notes, e.Tags, e.Lemma, e.Phonetic, e.PartOfSpeech, e.Level, e.Definition, e.Synonyms, e.Examples, e.Explanation, e.DictionarySource, e.ScreenshotFile, e.AudioFile }.Select(Field))));
    }
    public static IReadOnlyList<VocabularyEntry> Import(string csv)
    {
        if (csv.Length > 20 * 1024 * 1024) throw new UserFacingException("Choose a CSV smaller than 20 MB.");
        var rows = Parse(csv.TrimStart('\uFEFF'));
        if (rows.Count == 0 || (!rows[0].SequenceEqual(Header) && !rows[0].SequenceEqual(Extended))) throw new UserFacingException("Use a GaameTranslate CSV export with Word, Translation, SourceLanguage, TargetLanguage, Context, TranslatedContext, Notes, Tags columns.");
        var entries = new List<VocabularyEntry>();
        foreach (var row in rows.Skip(1).Where(r => r.Any(s => s.Length > 0)))
        {
            if (row.Count != rows[0].Count) throw new UserFacingException("A CSV row has the wrong number of columns. Check quoted fields.");
            static string Unescape(string v) => v.Length > 1 && v[0] == '\'' && "=+-@\t\r".Contains(v[1]) ? v[1..] : v;
            var r = row.Select(Unescape).ToArray();
            if (string.IsNullOrWhiteSpace(r[0]) || string.IsNullOrWhiteSpace(r[1])) throw new UserFacingException("Every imported row needs a word and translation.");
            string Field(int i) => r.Length > i ? r[i] : "";
            entries.Add(new() { Word = r[0], Translation = r[1], SourceLanguage = r[2], TargetLanguage = r[3], Context = r[4], TranslatedContext = r[5], Notes = r[6], Tags = r[7], Lemma = Field(8), Phonetic = Field(9), PartOfSpeech = Field(10), Level = Field(11), Definition = Field(12), Synonyms = Field(13), Examples = Field(14), Explanation = Field(15), DictionarySource = Field(16), ScreenshotFile = Field(17), AudioFile = Field(18) });
        }
        return entries;
    }
    private static List<List<string>> Parse(string csv)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (c == '"') { if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((c == '\r' || c == '\n') && !quoted)
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear(); rows.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (quoted) throw new UserFacingException("CSV contains an unclosed quoted field.");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
    public static string ExportAnki(IEnumerable<VocabularyEntry> entries)
    {
        static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        return "#separator:tab\n#html:false\n" + string.Join('\n', entries.Select(e => $"{Clean(e.Word)}\t{Clean(e.Translation)}\t{Clean(e.Context)}\t{Clean(e.Tags)}"));
    }
}
