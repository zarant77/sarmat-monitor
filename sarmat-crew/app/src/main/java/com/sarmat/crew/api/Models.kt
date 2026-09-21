package com.sarmat.crew.api

data class CrewUser(
    val username: String,
    val role: String,
    val crewName: String,
    val crewNumber: Int?,
)

data class BatterySummary(
    val id: String,
    val label: String,
    val serialNumber: String,
    val typeName: String,
    val capacityAh: Double,
    val minVoltage: Double,
    val maxVoltage: Double,
    val cellCount: Int,
    val state: String,
    val cycleCount: Int,
    val latestTotalVoltage: Double?,
    val latestChargePercent: Int?,
    val latestDelta: Double?,
    val latestHealth: String?,
)
