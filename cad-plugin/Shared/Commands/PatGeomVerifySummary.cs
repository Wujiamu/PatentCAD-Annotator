using System;
using System.Globalization;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Shared, host-neutral summary rules for PATGEOMVERIFY. Empty drawings
    /// are SKIP unless required counts were declared; any required target
    /// missing or unreadable object is FAIL. A strict run converts SKIP into a
    /// gate rejection without pretending that geometry passed. The external
    /// verifier, rather than the CAD command, owns the nonzero process exit.
    /// </summary>
    public static class PatGeomVerifySummary
    {
        public const string Pass = "PASS";
        public const string Fail = "FAIL";
        public const string Skip = "SKIP";

        public sealed class Result
        {
            public string Status = Skip;
            public bool RequiresFailureExit;
            public int MissingTargets;
            public string Detail = "";
        }

        public static Result Evaluate(
            int braces,
            int bracesPassed,
            int bracesFailed,
            int bracesUnreadable,
            int texts,
            int textsPassed,
            int textsFailed,
            int textsUnreadable,
            int requiredBraces,
            int requiredTexts,
            bool strict)
        {
            Result result = new Result();
            int missingBraces = Math.Max(0, requiredBraces - braces);
            int missingTexts = Math.Max(0, requiredTexts - texts);
            result.MissingTargets = missingBraces + missingTexts;
            int failures = bracesFailed + textsFailed + bracesUnreadable + textsUnreadable;

            result.Detail = string.Format(CultureInfo.InvariantCulture,
                "braces={0} passed={1} failed={2} unreadable={3}; texts={4} passed={5} failed={6} unreadable={7}; requiredBraces={8} requiredTexts={9}; strict={10}",
                braces, bracesPassed, bracesFailed, bracesUnreadable,
                texts, textsPassed, textsFailed, textsUnreadable,
                requiredBraces, requiredTexts, strict ? "true" : "false");

            if (failures > 0)
            {
                result.Status = Fail;
                result.RequiresFailureExit = true;
                result.Detail = "failed or unreadable assertions; " + result.Detail;
                return result;
            }

            if (result.MissingTargets > 0)
            {
                result.Status = Fail;
                result.RequiresFailureExit = true;
                result.Detail = "required targets missing; " + result.Detail;
                return result;
            }

            if (braces == 0 && texts == 0)
            {
                result.Status = Skip;
                result.RequiresFailureExit = strict;
                result.Detail = (strict ? "strict run has no applicable targets; "
                    : "no applicable targets (daily empty-drawing check); ") + result.Detail;
                return result;
            }

            result.Status = Pass;
            result.RequiresFailureExit = false;
            return result;
        }
    }
}
