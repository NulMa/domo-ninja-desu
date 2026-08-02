// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using System.Collections.Generic;

namespace DomoNinja.Core.Data
{
    /// <summary>검증 규칙 위반 한 건. `_schema` §6 의 항목 하나에 대응한다.</summary>
    public readonly struct ValidationError
    {
        /// <summary>규칙 번호 (R01~R20). `data/_schema/README.md` §6 의 순서와 같다.</summary>
        public readonly string Rule;

        /// <summary>어디가 틀렸는지 (예: "skills.json C3-B").</summary>
        public readonly string Where;

        public readonly string Message;

        public ValidationError(string rule, string where, string message)
        {
            Rule = rule;
            Where = where;
            Message = message;
        }

        public override string ToString() => $"[{Rule}] {Where} — {Message}";
    }

    /// <summary>
    /// 데이터 검증 실패. <b>조용히 기본값으로 넘어가지 않는다.</b> (`_schema` 머리말)
    /// </summary>
    /// <remarks>
    /// ★ 첫 위반에서 멈추지 않고 <b>전부 모아서</b> 던지는 이유가 있다.
    /// 이 JSON 들은 사람만 고치는 게 아니라 최적화기(P4)가 값을 바꿔 써넣는다.
    /// 한 번에 하나씩만 알려주면 고치고 돌리기를 반복해야 하고, CI 한 바퀴가 그만큼 곱해진다.
    /// 검증은 로드 시점 1회뿐이라 전부 모으는 비용은 무시할 수 있다.
    /// </remarks>
    public sealed class DataValidationException : Exception
    {
        public IReadOnlyList<ValidationError> Errors { get; }

        public DataValidationException(IReadOnlyList<ValidationError> errors)
            : base(Format(errors))
        {
            Errors = errors;
        }

        private static string Format(IReadOnlyList<ValidationError> errors)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("데이터 검증 실패 ").Append(errors.Count).AppendLine("건 (data/_schema/README.md §6)");
            foreach (var e in errors)
            {
                sb.Append("  ").AppendLine(e.ToString());
            }
            return sb.ToString();
        }
    }
}
