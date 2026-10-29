using System.Text;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// Read-only access to CM2 letter/report files under Cm2ReportRoot.
/// Never edits, renames, moves, overwrites, or converts the original source file.
/// Extraction happens only in memory (or ephemeral process memory).
/// </summary>
public static class ResearchCm2DocumentReader
{
    public static ResearchDocumentExtract ReadLetter(ICm2Repository cm2, ResearchCm2LetterRef letter)
    {
        var kind = ResearchDocumentKind.ConsultLetter;
        var fileName = Path.GetFileName(ResearchSourceFileFingerprint.NormalizePath(letter.RelativePath));
        var baseDoc = new ResearchSourceDocument(
            kind,
            letter.PatientRid,
            letter.LettersRid,
            letter.LetterDate,
            letter.RelativePath,
            fileName,
            letter.LetterTitle,
            Reviewed: false,
            ConsultRid: letter.ConsultRid,
            LetterType: letter.LetterType);

        if (string.IsNullOrWhiteSpace(letter.RelativePath))
        {
            return new ResearchDocumentExtract(
                baseDoc with { ExtractError = "LETTER_PATH is empty for this consultation letter." },
                null);
        }

        if (!cm2.TryResolveCm2FilePath(letter.RelativePath, out var fullPath, out var resolveError))
        {
            return new ResearchDocumentExtract(
                baseDoc with { ExtractError = resolveError },
                null);
        }

        if (!TryReadFileBytesReadOnly(fullPath, out var bytes, out var readError))
        {
            return new ResearchDocumentExtract(
                baseDoc with { ExtractError = readError },
                null);
        }

        var extracted = ExtractText(fullPath, bytes);
        if (string.IsNullOrWhiteSpace(extracted))
        {
            return new ResearchDocumentExtract(
                baseDoc with
                {
                    FileName = Path.GetFileName(fullPath),
                    ExtractError = "Could not extract usable text from the consultation letter RTF.",
                },
                null);
        }

        return new ResearchDocumentExtract(
            baseDoc with
            {
                FileName = Path.GetFileName(fullPath),
                Reviewed = true,
            },
            extracted);
    }

    public static ResearchDocumentExtract ReadStudyReport(
        ICm2Repository cm2, ResearchCm2StudyReportRef report, string kind)
    {
        var fileName = Path.GetFileName(ResearchSourceFileFingerprint.NormalizePath(report.RelativePath));
        var baseDoc = new ResearchSourceDocument(
            kind,
            report.PatientRid,
            report.TestRid,
            report.TestDate,
            report.RelativePath,
            fileName,
            report.InvestigationName,
            Reviewed: false,
            TestRequired: report.TestRequired);

        if (string.IsNullOrWhiteSpace(report.RelativePath))
        {
            return new ResearchDocumentExtract(
                baseDoc with { ExtractError = "REPORT_PATH is empty for this investigation." },
                null);
        }

        if (!cm2.TryResolveCm2FilePath(report.RelativePath, out var fullPath, out var resolveError))
        {
            return new ResearchDocumentExtract(
                baseDoc with { ExtractError = resolveError },
                null);
        }

        if (!TryReadFileBytesReadOnly(fullPath, out var bytes, out var readError))
        {
            return new ResearchDocumentExtract(
                baseDoc with { ExtractError = readError },
                null);
        }

        var extracted = ExtractText(fullPath, bytes);
        if (string.IsNullOrWhiteSpace(extracted))
        {
            return new ResearchDocumentExtract(
                baseDoc with
                {
                    FileName = Path.GetFileName(fullPath),
                    ExtractError = "Could not extract usable text from the investigation report.",
                },
                null);
        }

        return new ResearchDocumentExtract(
            baseDoc with
            {
                FileName = Path.GetFileName(fullPath),
                Reviewed = true,
            },
            extracted);
    }

    /// <summary>
    /// Opens the source file with read-only access and shared read.
    /// Never uses write, create, truncate, move, or delete APIs on the source path.
    /// </summary>
    private static bool TryReadFileBytesReadOnly(string fullPath, out byte[] bytes, out string error)
    {
        bytes = [];
        try
        {
            if (!File.Exists(fullPath))
            {
                error = $"Source file not found at {fullPath}.";
                return false;
            }

            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            bytes = ms.ToArray();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not read source file (read-only): {ex.Message}";
            return false;
        }
    }

    private static string ExtractText(string fullPath, byte[] bytes)
    {
        var ext = Path.GetExtension(fullPath);
        if (ext.Equals(".rtf", StringComparison.OrdinalIgnoreCase)
            || LooksLikeRtf(bytes))
        {
            var rtf = Encoding.Latin1.GetString(bytes);
            return RtfTextExtractor.Extract(rtf);
        }

        // Plain-ish fallback for unexpected extensions; still never mutates the source.
        return Encoding.Latin1.GetString(bytes).Trim();
    }

    private static bool LooksLikeRtf(byte[] bytes)
    {
        if (bytes.Length < 5)
            return false;
        return bytes[0] == (byte)'{'
            && bytes[1] == (byte)'\\'
            && bytes[2] == (byte)'r'
            && bytes[3] == (byte)'t'
            && bytes[4] == (byte)'f';
    }

    public static string BuildLabeledNotes(
        IReadOnlyList<(ResearchSourceDocument Doc, string Text)> parts,
        ResearchDerivedFacts? facts = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CLINICAL SOURCE DOCUMENTS (CM2). Assess only what is stated below.");
        sb.AppendLine("Do not invent facts. If a criterion is not addressed, mark UNCLEAR");
        sb.AppendLine("and use reason \"Unable to determine from available records\".");
        sb.AppendLine("If height and weight are present, calculate BMI and use it for BMI criteria.");
        sb.AppendLine("Current/prior GLP-1/GIP/amylin use counts only when a named agent is documented as taken");
        sb.AppendLine($"(e.g. {ResearchClinicalFactsExtractor.AgentSearchPhrases}).");
        sb.AppendLine("Discussion or recommendation to start does not count as having taken the therapy.");
        sb.AppendLine();
        if (facts is not null)
            sb.Append(ResearchClinicalFactsExtractor.FormatBlock(facts));

        foreach (var (doc, text) in parts)
        {
            sb.AppendLine("===== SOURCE BEGIN =====");
            sb.AppendLine($"kind: {doc.Kind}");
            if (!string.IsNullOrWhiteSpace(doc.SourceRid))
                sb.AppendLine($"sourceRid: {doc.SourceRid}");
            if (doc.SourceDate is not null)
                sb.AppendLine($"date: {doc.SourceDate:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(doc.RelativePath))
                sb.AppendLine($"path: {doc.RelativePath}");
            if (!string.IsNullOrWhiteSpace(doc.FileName))
                sb.AppendLine($"fileName: {doc.FileName}");
            if (!string.IsNullOrWhiteSpace(doc.Title))
                sb.AppendLine($"title: {doc.Title}");
            sb.AppendLine("----- content -----");
            sb.AppendLine(text.Trim());
            sb.AppendLine("===== SOURCE END =====");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
