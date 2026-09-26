package com.sarmat.crew

const val DEFAULT_DISCHARGED_THRESHOLD_PERCENT = 50

fun isBatteryDischarged(state: String, chargePercent: Int?, dischargedThresholdPercent: Int): Boolean =
    state == "ready" && chargePercent != null && chargePercent <= dischargedThresholdPercent
