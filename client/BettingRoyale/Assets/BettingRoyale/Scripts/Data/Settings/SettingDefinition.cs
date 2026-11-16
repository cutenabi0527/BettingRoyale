using System;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 설정 한 항목의 정적 정의. 데이터 주도 UI 구성과 값 검증의 단일 출처다.
    /// 정의는 불변이며 런타임 상태를 갖지 않는다.
    /// </summary>
    public sealed class SettingDefinition
    {
        private static readonly string[] EmptyChoices = new string[0];

        private readonly string[] _choices;

        public SettingDefinition(
            string id,
            string label,
            string description,
            SettingsCategory category,
            SettingKind kind,
            string defaultValue,
            float min = 0f,
            float max = 0f,
            float step = 0f,
            string[] choices = null,
            bool requiresRestart = false,
            string platform = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("설정 ID는 비어 있을 수 없습니다.", nameof(id));
            }

            Id = id;
            Label = label ?? string.Empty;
            Description = description ?? string.Empty;
            Category = category;
            Kind = kind;
            DefaultValue = defaultValue ?? string.Empty;
            Min = min;
            Max = max;
            Step = step;
            _choices = choices != null && choices.Length > 0 ? (string[])choices.Clone() : EmptyChoices;
            RequiresRestart = requiresRestart;
            Platform = platform ?? string.Empty;
        }

        public string Id { get; }

        public string Label { get; }

        public string Description { get; }

        public SettingsCategory Category { get; }

        public SettingKind Kind { get; }

        public string DefaultValue { get; }

        public float Min { get; }

        public float Max { get; }

        public float Step { get; }

        /// <summary>
        /// 고정 선택지 목록. 비어 있으면 런타임 하드웨어 능력으로 채우는 동적 선택지다.
        /// 호출자가 내부 배열을 바꾸지 못하도록 복사본을 돌려준다.
        /// </summary>
        public string[] Choices
        {
            get { return _choices.Length == 0 ? EmptyChoices : (string[])_choices.Clone(); }
        }

        public bool RequiresRestart { get; }

        /// <summary>빈 문자열이면 모든 플랫폼. 그 외에는 해당 플랫폼에서만 노출한다.</summary>
        public string Platform { get; }

        /// <summary>동적 선택지(해상도/주사율 등) 여부.</summary>
        public bool HasDynamicChoices
        {
            get { return Kind == SettingKind.Choice && _choices.Length == 0; }
        }

        public bool HasChoice(string value)
        {
            if (_choices.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < _choices.Length; i++)
            {
                if (string.Equals(_choices[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public float Clamp(float value)
        {
            if (Kind != SettingKind.Slider)
            {
                return value;
            }

            if (value < Min)
            {
                return Min;
            }

            return value > Max ? Max : value;
        }

        public override string ToString()
        {
            return Id + "(" + Kind + ")";
        }
    }
}
