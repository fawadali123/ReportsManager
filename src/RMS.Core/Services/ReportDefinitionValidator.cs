using RMS.Core.Domain;
using RMS.Core.Reporting;

namespace RMS.Core.Services;

/// <summary>Checks a report definition, including its parameter settings, before an administrator saves it.</summary>
public static class ReportDefinitionValidator
{
    /// <returns>Messages to show on the edit screen. Empty when the definition is fine.</returns>
    /// <param name="database">The database the report will run on; it decides which SQL dialect is expected.</param>
    public static IReadOnlyList<string> Validate(
        Report report, ReportingOptions options, DateTime now, DatabaseKind database = DatabaseKind.Oracle)
    {
        var errors = new List<string>(ReportSqlValidator.Validate(report.SqlQuery, report.QueryType, database));

        if (report.QueryType == ReportQueryType.StoredProcedure && !options.AllowStoredProcedures)
        {
            errors.Add("Stored procedure reports are switched off in the configuration.");
        }

        foreach (var parameter in report.Parameters)
        {
            var name = "@" + parameter.Name;
            var hasLookup = !string.IsNullOrWhiteSpace(parameter.LookupSource);

            if (parameter.AllowMultiple && !hasLookup)
            {
                errors.Add($"{name}: add a list of choices before allowing several values.");
            }

            if (parameter.AllowMultiple && parameter.DataType == ParameterDataType.Boolean)
            {
                errors.Add($"{name}: a yes/no parameter cannot take several values.");
            }

            if (hasLookup && LookupSource.IsQuery(parameter.LookupSource))
            {
                errors.AddRange(ReportSqlValidator.Validate(parameter.LookupSource, ReportQueryType.Select, database)
                    .Select(e => $"{name} choices: {e}"));

                var formParameters = ReportParameterResolver.GetParameterNames(parameter.LookupSource, database);
                if (formParameters.Count > 0)
                {
                    errors.Add($"{name} choices: the query uses @{formParameters[0]}. A choices query can only use system parameters such as @{SystemParameters.Username}.");
                }
            }
            else if (hasLookup && LookupSource.ParseStatic(parameter.LookupSource).Count == 0)
            {
                errors.Add($"{name}: the list of choices is empty.");
            }

            if (!hasLookup && !string.IsNullOrWhiteSpace(parameter.DefaultValue))
            {
                try
                {
                    ParameterValueConverter.Convert(
                        DefaultValueResolver.Resolve(parameter.DefaultValue, parameter.DataType, now), parameter.DataType);
                }
                catch (FormatException ex)
                {
                    var isDate = parameter.DataType is ParameterDataType.Date or ParameterDataType.DateTime;
                    errors.Add(isDate
                        ? $"{name} default value: use a date such as 2026-01-31, or a token such as TODAY, START_OF_MONTH or TODAY-7."
                        : $"{name} default value: {ex.Message}");
                }
            }
        }

        return errors;
    }
}
