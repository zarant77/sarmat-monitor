package com.sarmat.crew

const val DEFAULT_DISCHARGED_THRESHOLD_PERCENT = 50
const val DEFAULT_CRITICAL_CHARGE_PERCENT = 20

enum class BatteryWidgetLevel { FULL, LOW, CRITICAL, UNKNOWN }

fun isBatteryDischarged(state: String, chargePercent: Int?, dischargedThresholdPercent: Int): Boolean =
    state == "ready" && chargePercent != null && chargePercent <= dischargedThresholdPercent

fun batteryWidgetLevel(chargePercent: Int?, dischargedThresholdPercent: Int, criticalChargePercent: Int): BatteryWidgetLevel = when {
    chargePercent == null -> BatteryWidgetLevel.UNKNOWN
    chargePercent < criticalChargePercent -> BatteryWidgetLevel.CRITICAL
    chargePercent > dischargedThresholdPercent -> BatteryWidgetLevel.FULL
    else -> BatteryWidgetLevel.LOW
}
