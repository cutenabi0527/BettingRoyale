package com.bettingroyale.server.medal;

import java.util.HashMap;
import java.util.Map;

import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * 순위별 메달 지급 정책. 수치는 코드가 아니라 application.yml 에 있다(docs/server/api.md 5절).
 * ⚠️ 현재 값은 전부 임시(docs/specs/backend-engineering/S-BE-ZK5DKZ.md).
 * 동점·이탈·봇·방 유형 정책은 TBD — 값이 정해지면 여기에 필드로 승격한다.
 */
@ConfigurationProperties(prefix = "medal")
public class MedalPolicy {

    private Map<Integer, Long> rankRewards = new HashMap<>();
    private long defaultReward;

    public long rewardFor(int rank) {
        return rankRewards.getOrDefault(rank, defaultReward);
    }

    public Map<Integer, Long> getRankRewards() {
        return rankRewards;
    }

    public void setRankRewards(Map<Integer, Long> rankRewards) {
        this.rankRewards = rankRewards;
    }

    public long getDefaultReward() {
        return defaultReward;
    }

    public void setDefaultReward(long defaultReward) {
        this.defaultReward = defaultReward;
    }
}
