namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 확정된 적중 하나가 만든 결과 (<see cref="BlessingCombatState.ConfirmHit"/>).
    ///
    /// <para>
    /// <b><see cref="ExtraDamage"/> 는 축복이 만든 피해다</b> — 다시 <c>ConfirmHit</c> 로 돌려보내면 안 된다.
    /// 그렇게 하면 표식이 표식을 터뜨리는 재귀가 생긴다.
    /// </para>
    /// </summary>
    public readonly struct BlessingHitResult
    {
        public BlessingHitResult(float extraDamage, float staggerSeconds, bool markApplied, bool markDetonated,
            float healAmount = 0f, float splashDamage = 0f, float splashRadius = 0f,
            float ricochetDamage = 0f, float ricochetRadius = 0f, bool tokenTossTriggered = false)
        {
            TokenTossTriggered = tokenTossTriggered;
            ExtraDamage = extraDamage;
            StaggerSeconds = staggerSeconds;
            MarkApplied = markApplied;
            MarkDetonated = markDetonated;
            HealAmount = healAmount;
            SplashDamage = splashDamage;
            SplashRadius = splashRadius;
            RicochetDamage = ricochetDamage;
            RicochetRadius = ricochetRadius;
        }

        /// <summary>기본 피해와 별도로 더 넣을 피해(표식 폭발 등). 0 이면 없다.</summary>
        public float ExtraDamage { get; }
        public bool TokenTossTriggered { get; }

        /// <summary>
        /// 이 적중이 주는 경직(초). 같은 입력의 <see cref="BlessingHit.StaggerSeconds"/> 와 같은 값이다 —
        /// <b>둘 다 적용하면 두 번 걸린다.</b> 실제 적용은 이쪽 값 하나만 쓴다.
        /// </summary>
        public float StaggerSeconds { get; }

        /// <summary>이 적중으로 표식이 새로 붙었는가.</summary>
        public bool MarkApplied { get; }

        /// <summary>이 적중으로 표식이 터졌는가. <see cref="ExtraDamage"/> 가 그 피해다.</summary>
        public bool MarkDetonated { get; }

        /// <summary>기본 적중 뒤 공격자에게 직접 회복할 양(잔돈 회수·마진 콜).</summary>
        public float HealAmount { get; }

        /// <summary>다른 대상에게 나눠 줄 피해와 반경. 0이면 없다.</summary>
        public float SplashDamage { get; }
        public float SplashRadius { get; }

        /// <summary>도탄할 한 대상에게 줄 피해와 반경. 0이면 없다.</summary>
        public float RicochetDamage { get; }
        public float RicochetRadius { get; }
    }
}
