using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 현재 목업 효과표의 표시 문구(S-UX-Z78WCK). 수치는 효과 행에서 읽으며 별도 밸런스를 갖지 않는다.
    /// 정식 콘텐츠 공급자로 교체할 때는 효과와 설명을 함께 교체한다.
    /// </summary>
    public static class PlaceholderBlessingDescription
    {
        public static string Create(IReadOnlyList<BlessingEffectRow> effects)
        {
            if (effects == null) throw new ArgumentNullException(nameof(effects));
            var text = new StringBuilder();
            foreach (BlessingEffectRow effect in effects)
            {
                string expectedStack = effect.ParamKey == DamageStatKeys.DamageTakenMult ? "MUL" : "ADD";
                string stack = string.IsNullOrEmpty(effect.Stack) ? "ADD" : effect.Stack;
                if (effect.IsConditional || !effect.TryGetNumber(out float value)
                    || float.IsNaN(value) || float.IsInfinity(value) || value < 0f
                    || stack != expectedStack)
                {
                    throw new ArgumentException("목업 설명이 지원하지 않는 효과입니다.", nameof(effects));
                }

                if (text.Length > 0) text.Append('\n');
                string number = Number(value);
                switch (effect.ParamKey)
                {
                    case DamageStatKeys.Defense:
                        text.Append($"방어력 +{number}.\n직접 공격 피해를 최대 {number}만큼 추가로 줄입니다. 적의 관통에 따라 감소량이 달라집니다.");
                        break;
                    case DamageStatKeys.DamageTakenMult:
                        if (value > 1f) throw new ArgumentException("받는 피해 감소 배율이 아닙니다.", nameof(effects));
                        text.Append($"받는 직접 공격 피해 {Number((1f - value) * 100f)}% 감소.\n기존 피해에 {number}배를 적용합니다. 자기장 피해에는 적용되지 않습니다.");
                        break;
                    case DamageStatKeys.AttackPower:
                        text.Append($"주무기 공격력 +{number}.\n기본 공격 피해에 {number}를 더합니다. 소모품 공격에는 적용되지 않습니다.");
                        break;
                    case DamageStatKeys.ArmorPen:
                        text.Append($"주무기 방어력 관통 +{number}.\n상대 방어력을 {number}만큼 무시합니다. 초과 관통은 추가 피해가 되지 않습니다.");
                        break;
                    case DamageStatKeys.CritDamage:
                        text.Append($"활의 치명타 피해 배율 +{number}.\n치명타가 발생한 기본 공격에 적용됩니다.");
                        break;
                    case "critical_every":
                        text.Append($"활 기본 공격 {number}발마다 한 발이 치명타가 됩니다.");
                        break;
                    case DamageStatKeys.Lifesteal:
                        text.Append($"흡혈 수치 +{Number(value * 100f)}%p.\n주무기로 실제로 깎은 체력에 비례해 회복합니다. 보호막·허수아비·추가 폭발은 제외합니다.");
                        break;
                    default:
                        throw new ArgumentException("설명이 없는 목업 효과입니다: " + effect.ParamKey, nameof(effects));
                }
            }
            return text.ToString();
        }

        private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
