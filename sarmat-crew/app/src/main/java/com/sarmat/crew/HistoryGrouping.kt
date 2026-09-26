package com.sarmat.crew

import com.sarmat.crew.api.BatteryHistoryItem

data class BatteryCycleGroup(
    val number: Int,
    val discharge: BatteryHistoryItem?,
    val charge: BatteryHistoryItem,
)

fun groupBatteryHistoryCycles(items: List<BatteryHistoryItem>): List<BatteryCycleGroup> {
    var pendingDischarge: BatteryHistoryItem? = null
    val groups = mutableListOf<BatteryCycleGroup>()
    items.sortedBy { it.occurredAt }.forEach { item ->
        if (item.kind == "discharge" && item.inferred == true) pendingDischarge = item
        if (item.kind == "charge" && item.inferred == true && (item.cycleDelta ?: 0) > 0) {
            groups += BatteryCycleGroup(groups.size + 1, pendingDischarge, item)
            pendingDischarge = null
        }
    }
    return groups
}
