package com.sarmat.crew

const val DEFAULT_DISCHARGED_THRESHOLD_PERCENT = 50
const val DEFAULT_CRITICAL_CHARGE_PERCENT = 20

enum class BatteryWidgetLevel { FULL, LOW, CRITICAL, UNKNOWN }

/** Keep the current battery's lightning distinct from the last removed battery. */
fun lastDroneBatteryId(batteries: List<com.sarmat.crew.api.BatterySummary>): String? =
    batteries.firstOrNull { it.activeSince != null }?.id ?: batteries.maxWithOrNull(compareBy<com.sarmat.crew.api.BatterySummary> {
        (it.activeSince ?: it.lastActiveSince)?.let(java.time.Instant::parse) ?: java.time.Instant.MIN
    }.thenBy { it.id })?.takeIf { it.activeSince != null || it.lastActiveSince != null }?.id

fun isBatteryDischarged(state: String, chargePercent: Int?, dischargedThresholdPercent: Int): Boolean =
    state == "ready" && chargePercent != null && chargePercent <= dischargedThresholdPercent

fun batteryWidgetLevel(chargePercent: Int?, dischargedThresholdPercent: Int, criticalChargePercent: Int): BatteryWidgetLevel = when {
    chargePercent == null -> BatteryWidgetLevel.UNKNOWN
    chargePercent < criticalChargePercent -> BatteryWidgetLevel.CRITICAL
    chargePercent > dischargedThresholdPercent -> BatteryWidgetLevel.FULL
    else -> BatteryWidgetLevel.LOW
}
