package com.sarmat.crew

import com.sarmat.crew.api.BatteryHistoryItem
import org.junit.Assert.assertEquals
import org.junit.Test

class HistoryGroupingTest {
    private fun event(id: String, kind: String, at: String, inferred: Boolean = true, cycleDelta: Int = 0) = BatteryHistoryItem(
        id = id, kind = kind, occurredAt = at, totalVoltage = null, cellVoltages = null,
        chargePercent = null, minCellVoltage = null, maxCellVoltage = null, cellDelta = null,
        health = null, warningThresholdV = null, dangerThresholdV = null, cycleDelta = cycleDelta,
        flightMinutes = null, inferred = inferred, fromCrewName = null, toCrewName = null, notes = null,
    )

    @Test fun `groups inferred discharge and charge events into chronological cycles`() {
        val items = listOf(
            event("charge-2", "charge", "2026-01-04T00:00:00Z", cycleDelta = 1),
            event("discharge-1", "discharge", "2026-01-01T00:00:00Z"),
            event("charge-1", "charge", "2026-01-02T00:00:00Z", cycleDelta = 1),
            event("discharge-2", "discharge", "2026-01-03T00:00:00Z"),
        )

        val cycles = groupBatteryHistoryCycles(items)

        assertEquals(listOf(1, 2), cycles.map { it.number })
        assertEquals(listOf("discharge-1", "discharge-2"), cycles.map { it.discharge?.id })
        assertEquals(listOf("charge-1", "charge-2"), cycles.map { it.charge.id })
    }

    @Test fun `ignores manual and non-completing charge events`() {
        val cycles = groupBatteryHistoryCycles(listOf(
            event("manual", "charge", "2026-01-01T00:00:00Z", inferred = false, cycleDelta = 1),
            event("partial", "charge", "2026-01-02T00:00:00Z", cycleDelta = 0),
        ))

        assertEquals(emptyList<BatteryCycleGroup>(), cycles)
    }
}
