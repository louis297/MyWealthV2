using System.Text;
using System.Text.Json;
using Cronos;
using FluentValidation.Results;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

public static class ReportScheduleRules
{
    public const int MaxParametersBytes = 8 * 1024;

    public const int MaxCronLength = 100;

    public static void EnsureShape(Guid reportId, JsonElement parameters, string? cron, bool? isActive)
    {
        if (reportId == Guid.Empty)
        {
            throw new ValidationException([
                new ValidationFailure("reportId", "reportId is required.")
            ]);
        }

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be an object.")
            ]);
        }

        if (Encoding.UTF8.GetByteCount(parameters.GetRawText()) > MaxParametersBytes)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters must be 8 KB or smaller.")
            ]);
        }

        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new ValidationException([
                new ValidationFailure("cron", "cron is required.")
            ]);
        }

        if (cron.Length > MaxCronLength)
        {
            throw new ValidationException([
                new ValidationFailure("cron", "cron must be 100 characters or fewer.")
            ]);
        }

        if (!IsStandardCron(cron))
        {
            throw new ValidationException([
                new ValidationFailure("cron", "cron must be a 5-field expression.")
            ]);
        }

        if (isActive is null)
        {
            throw new ValidationException([
                new ValidationFailure("isActive", "isActive is required.")
            ]);
        }
    }

    public static bool IsStandardCron(string cron)
    {
        try
        {
            _ = CronExpression.Parse(cron, CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
