using System.Collections.Generic;
using System.Linq;

namespace UDiscord.Core.Utility
{
    public sealed class ValidationResult
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> Warnings => _warnings;
        public bool IsValid => _errors.Count == 0;

        public void AddError(string error)
        {
            if (!string.IsNullOrWhiteSpace(error)) _errors.Add(error.Trim());
        }

        public void AddWarning(string warning)
        {
            if (!string.IsNullOrWhiteSpace(warning)) _warnings.Add(warning.Trim());
        }

        public override string ToString()
        {
            return string.Join("; ", _errors.Concat(_warnings).ToArray());
        }
    }
}
