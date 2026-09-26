package com.sarmat.crew

import android.graphics.Typeface
import android.icu.text.Collator
import android.icu.text.RuleBasedCollator
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import android.widget.*
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import com.sarmat.crew.api.ApiException
import com.sarmat.crew.api.BatteryHistoryItem
import com.sarmat.crew.api.BatterySummary
import com.sarmat.crew.api.CrewApi
import java.util.Locale
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.concurrent.Executors
import kotlin.math.roundToInt

class MainActivity : AppCompatActivity() {
    private enum class Screen { LOGIN, BATTERIES, MEASUREMENT, HISTORY }

    private lateinit var api: CrewApi
    private val networkExecutor = Executors.newSingleThreadExecutor()
    private val batteryLabelComparator = (Collator.getInstance(Locale.ROOT) as RuleBasedCollator).apply {
        setNumericCollation(true)
    }
    private var screen = Screen.LOGIN
    private var battery: BatterySummary? = null
    private var draft = mutableListOf<String>()
    private var draftNotes = ""
    private val cellViews = mutableListOf<TextView>()
    private val cellContainers = mutableListOf<LinearLayout>()
    private var selectedCell = 0
    private var manualReferenceCell: Int? = null
    private var editorMinCentivolts = 300
    private var editorMaxCentivolts = 424
    private var previewValid = false
    private var previewGeneration = 0
    private val previewHandler = Handler(Looper.getMainLooper())
    private var previewRunnable: Runnable? = null
    private var historyOffset = 0
    private var historyLoading = false
    private var historyHasMore = true

    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        api = CrewApi(getSharedPreferences("sarmat_crew", MODE_PRIVATE))
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                when (screen) {
                    Screen.HISTORY -> showMeasurement()
                    Screen.MEASUREMENT -> showBatteries()
                    else -> { isEnabled = false; onBackPressedDispatcher.onBackPressed() }
                }
            }
        })
        if (api.hasSession()) showBatteries() else showLogin()
    }

    private fun showLogin(message: String = "") {
        screen = Screen.LOGIN; setScreenContent(R.layout.activity_login)
        val server = findViewById<EditText>(R.id.serverUrlInput).apply { setText(api.baseUrl) }
        val username = findViewById<EditText>(R.id.usernameInput)
        val password = findViewById<EditText>(R.id.passwordInput)
        val error = findViewById<TextView>(R.id.loginError).apply { text = message }
        val button = findViewById<Button>(R.id.loginButton)
        button.setOnClickListener {
            button.isEnabled = false; error.text = ""
            networkExecutor.execute {
                runCatching { api.login(server.text.toString(), username.text.toString(), password.text.toString()) }
                    .onSuccess { runOnUiThread(::showBatteries) }
                    .onFailure { runOnUiThread { error.text = it.message ?: "Не вдалося увійти"; button.isEnabled = true } }
            }
        }
    }

    private fun showBatteries() {
        screen = Screen.BATTERIES; setScreenContent(R.layout.activity_batteries)
        findViewById<TextView>(R.id.crewNameText).text = api.savedCrewLabel()
        findViewById<TextView>(R.id.menuButton).setOnClickListener { anchor ->
            PopupMenu(this, anchor).apply {
                menu.add("Оновити").setOnMenuItemClickListener { loadBatteries(); true }
                menu.add("Вийти").setOnMenuItemClickListener {
                    networkExecutor.execute { api.logout(); runOnUiThread { showLogin() } }; true
                }
                show()
            }
        }
        loadBatteries()
    }

    private fun loadBatteries() {
        val list = findViewById<LinearLayout>(R.id.batteryList)
        val statusView = findViewById<TextView>(R.id.listStatusText).apply { text = if (list.childCount == 0) getString(R.string.loading) else "Оновлення…" }
        networkExecutor.execute {
            runCatching(api::batteries).onSuccess { items -> runOnUiThread {
                if (screen != Screen.BATTERIES) return@runOnUiThread
                statusView.text = if (items.isEmpty()) "У екіпажу немає активних батарей" else "${items.size} батарей"
                list.removeAllViews()
                items.sortedWith(compareBy<BatterySummary, String>(batteryLabelComparator) { it.label }.thenBy { it.id })
                    .forEach { list.addView(batteryCard(it)) }
            } }.onFailure { failure -> runOnUiThread {
                if (failure is ApiException && failure.status == 401) showLogin("Сесія завершилась. Увійдіть знову.")
                else statusView.text = failure.message ?: "Не вдалося завантажити батареї"
            } }
        }
    }

    private fun batteryCard(item: BatterySummary): View {
        val frame = FrameLayout(this).apply { layoutParams = LinearLayout.LayoutParams(-1, dp(108)).apply { setMargins(0, 0, 0, dp(8)) }; background = ContextCompat.getDrawable(this@MainActivity, R.drawable.action_background) }
        val actionLabel = if (item.activeSince == null) "У ДРОН" else "ЗНЯТИ"
        frame.addView(swipeActionLabel(actionLabel, Gravity.CENTER_VERTICAL or Gravity.START).apply { setPadding(dp(24), 0, 0, 0) }, FrameLayout.LayoutParams(-1, -1))
        frame.addView(swipeActionLabel(actionLabel, Gravity.CENTER_VERTICAL or Gravity.END).apply { setPadding(0, 0, dp(24), 0) }, FrameLayout.LayoutParams(-1, -1))
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL; setPadding(dp(16), dp(11), dp(16), dp(10))
            background = ContextCompat.getDrawable(this@MainActivity, if (item.activeSince != null) R.drawable.card_active else R.drawable.cell_background)
            val titleRow = LinearLayout(this@MainActivity).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
            titleRow.addView(TextView(this@MainActivity).apply {
                text = if (item.activeSince != null) "⚡ ${item.label}" else item.label; textSize = 19f; setTypeface(typeface, Typeface.BOLD); setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary))
            }, LinearLayout.LayoutParams(0, -2, 1f))
            titleRow.addView(TextView(this@MainActivity).apply {
                text = item.latestChargePercent?.let { "$it%" } ?: "—"; textSize = 20f; setTypeface(typeface, Typeface.BOLD); setTextColor(ContextCompat.getColor(this@MainActivity, R.color.lime))
            })
            addView(titleRow)
            addView(TextView(this@MainActivity).apply {
                text = if (item.activeSince != null) "У ДРОНІ · ${age(item.activeSince)}" else item.latestDelta?.let { String.format(Locale.US, "Δ %.2f V", it) } ?: "Немає вимірювань"
                setTextColor(ContextCompat.getColor(this@MainActivity, when (item.latestHealth) { "danger" -> R.color.status_danger; "warning" -> R.color.status_warning; else -> R.color.text_primary }))
                textSize = 15f; setPadding(0, dp(5), 0, 0)
            })
            addView(TextView(this@MainActivity).apply {
                text = buildString {
                    if (item.activeSince != null) item.latestDelta?.let { append(String.format(Locale.US, "Δ %.2f V · ", it)) }
                    append(item.latestMeasuredAt?.let { "Перевірена ${age(it)} тому" } ?: "Ще не перевірялась")
                }
                setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_muted)); textSize = 13f
            })
        }
        frame.addView(content, FrameLayout.LayoutParams(-1, -1))
        val touchSlop = ViewConfiguration.get(this).scaledTouchSlop
        var downX = 0f; var downY = 0f; var horizontalGesture = false; var verticalGesture = false
        content.setOnTouchListener { view, event ->
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = event.rawX; downY = event.rawY; horizontalGesture = false; verticalGesture = false
                    view.parent?.requestDisallowInterceptTouchEvent(true)
                    true
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = event.rawX - downX; val dy = event.rawY - downY
                    if (!horizontalGesture && !verticalGesture) {
                        when (classifyGesture(dx, dy, touchSlop)) {
                            GestureDirection.HORIZONTAL -> horizontalGesture = true
                            GestureDirection.VERTICAL -> {
                                verticalGesture = true; view.translationX = 0f
                                view.parent?.requestDisallowInterceptTouchEvent(false)
                            }
                            GestureDirection.UNDECIDED -> Unit
                        }
                    }
                    if (horizontalGesture) {
                        view.parent?.requestDisallowInterceptTouchEvent(true)
                        view.translationX = dx.coerceIn(-view.width * .7f, view.width * .7f)
                    }
                    !verticalGesture
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    view.parent?.requestDisallowInterceptTouchEvent(false)
                    val toggle = horizontalGesture && kotlin.math.abs(view.translationX) > dp(88)
                    view.animate().translationX(0f).setDuration(160).start()
                    if (toggle) toggleActive(item)
                    else if (!horizontalGesture && !verticalGesture && event.actionMasked == MotionEvent.ACTION_UP) openBattery(item)
                    !verticalGesture
                }
                else -> false
            }
        }
        return frame
    }

    private fun openBattery(item: BatterySummary) { battery = item; draft = MutableList(item.cellCount) { "" }; draftNotes = ""; manualReferenceCell = null; showMeasurement() }

    private fun toggleActive(item: BatterySummary) {
        findViewById<TextView>(R.id.listStatusText).text = if (item.activeSince == null) "Встановлюю ${item.label}…" else "Знімаю ${item.label}…"
        networkExecutor.execute { runCatching { api.toggleActive(item.id) }.onSuccess { runOnUiThread { if (screen == Screen.BATTERIES) loadBatteries() } }.onFailure { error -> runOnUiThread { if (screen == Screen.BATTERIES) findViewById<TextView>(R.id.listStatusText).text = error.message ?: "Не вдалося змінити активну батарею" } } }
    }

    private fun showMeasurement() {
        val item = battery ?: return showBatteries()
        screen = Screen.MEASUREMENT; setScreenContent(R.layout.activity_measurement)
        findViewById<TextView>(R.id.batteryTitle).text = item.label
        findViewById<TextView>(R.id.detailCharge).text = item.latestChargePercent?.let { "$it%" } ?: "—"
        findViewById<TextView>(R.id.batteryStatus).text = buildString {
            append(item.latestDelta?.let { String.format(Locale.US, "Δ %.2f V", it) } ?: "Немає вимірювань")
            item.latestHealth?.let { append(" · ${healthLabel(it)}") }
            item.latestMeasuredAt?.let { append("\nПеревірена ${age(it)} тому") }
        }
        findViewById<TextView>(R.id.batteryMeta).text = "${item.cellCount}S ${item.chemistry} · ${item.capacityAh.toInt()} Ah · ${item.serialNumber}"
        findViewById<Button>(R.id.historyButton).setOnClickListener { keepDraft(); showHistory() }
        configureVoltageEditor()
        createCells(item.cellCount)
        findViewById<EditText>(R.id.notesInput).setText(draftNotes)
        findViewById<Button>(R.id.saveMeasurementButton).apply { isEnabled = false; setOnClickListener { saveMeasurement() } }
        updateSummary()
    }

    private fun createCells(count: Int) {
        val container = findViewById<LinearLayout>(R.id.measurementCells); cellViews.clear(); cellContainers.clear()
        draft = MutableList(count) { draft.getOrNull(it).orEmpty() }
        for (start in 0 until count step 6) {
            container.addView(TextView(this).apply { text = "МОДУЛЬ ${if (start == 0) "A · 1–6" else "B · 7–12"}"; textSize = 12f; setTextColor(ContextCompat.getColor(this@MainActivity, R.color.lime)); setTypeface(typeface, Typeface.BOLD); setPadding(dp(4), dp(4), 0, dp(2)) })
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            for (index in start until minOf(start + 6, count)) {
                val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER; background = ContextCompat.getDrawable(this@MainActivity, R.drawable.cell_background); layoutParams = LinearLayout.LayoutParams(0, dp(58), 1f).apply { setMargins(dp(2), 0, dp(2), 0) }; isClickable = true; isFocusable = true; setOnClickListener { openCellEditor(index) } }
                box.addView(TextView(this).apply { text = "${index + 1}"; gravity = Gravity.CENTER; setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_muted)) })
                val value = TextView(this).apply { text = draft[index].toDoubleOrNull()?.let { String.format(Locale.US, "%.2f", it) } ?: "—"; gravity = Gravity.CENTER; textSize = 14f; setTypeface(typeface, Typeface.BOLD); setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary)) }
                box.addView(value, LinearLayout.LayoutParams(-1, dp(34))); row.addView(box); cellViews += value; cellContainers += box
            }
            container.addView(row)
        }
    }

    private fun updateSummary() {
        if (screen != Screen.MEASUREMENT || cellViews.isEmpty()) return
        cellViews.forEachIndexed { index, view -> view.text = draft[index].toDoubleOrNull()?.let { String.format(Locale.US, "%.2f", it) } ?: "—" }
        val values = draft.mapNotNull { it.toDoubleOrNull() }
        previewValid = false; findViewById<Button>(R.id.saveMeasurementButton).isEnabled = false
        findViewById<TextView>(R.id.measurementSummary).apply {
            visibility = if (values.isEmpty()) View.GONE else View.VISIBLE
            text = if (values.isNotEmpty()) String.format(Locale.US, "Загальна: %.2f V  ·  Min: %.2f V  ·  Max: %.2f V\nDelta: %.2f V", values.sum(), values.min(), values.max(), values.max() - values.min()) else ""
        }
        previewRunnable?.let(previewHandler::removeCallbacks)
        if (values.size == 12) {
            previewRunnable = Runnable { requestPreview(values) }.also { previewHandler.postDelayed(it, 250) }
        }
    }

    private fun keepDraft() {
        draftNotes = findViewById<EditText>(R.id.notesInput).text.toString()
    }

    private fun saveMeasurement() {
        val item = battery ?: return
        val error = findViewById<TextView>(R.id.measurementError); val button = findViewById<Button>(R.id.saveMeasurementButton)
        val nullable = draft.map { it.toDoubleOrNull() }
        if (nullable.any { it == null }) { error.text = "Заповніть усі ${item.cellCount} комірок"; return }
        val cells = nullable.filterNotNull(); val min = item.minVoltage / item.cellCount; val max = item.maxVoltage / item.cellCount + 0.04
        if (cells.any { it !in min..max }) { error.text = String.format(Locale.US, "Допустимий діапазон: %.2f–%.2f V", min, max); return }
        if (!previewValid) { error.text = "Дочекайтеся перевірки вимірювання"; return }
        button.isEnabled = false; error.text = "Зберігаю…"
        val notes = findViewById<EditText>(R.id.notesInput).text.toString()
        networkExecutor.execute {
            runCatching { api.saveMeasurement(item.id, cells, notes); api.batteries().first { it.id == item.id } }.onSuccess { updated -> runOnUiThread { battery = updated; draft = MutableList(updated.cellCount) { "" }; draftNotes = ""; manualReferenceCell = null; showMeasurement() } }
                .onFailure { runOnUiThread { error.text = it.message ?: "Не вдалося зберегти"; button.isEnabled = true } }
        }
    }

    private fun configureVoltageEditor() {
        val slider = findViewById<SeekBar>(R.id.voltageSlider)
        slider.max = editorMaxCentivolts - editorMinCentivolts
        slider.isEnabled = false
        slider.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar, progress: Int, fromUser: Boolean) { if (fromUser) setSelectedVoltage((editorMinCentivolts + progress) / 100.0) }
            override fun onStartTrackingTouch(seekBar: SeekBar) = Unit
            override fun onStopTrackingTouch(seekBar: SeekBar) = Unit
        })
        findViewById<Button>(R.id.decreaseVoltage).apply { isEnabled = false; setOnClickListener { adjustSelected(-0.01) } }
        findViewById<Button>(R.id.increaseVoltage).apply { isEnabled = false; setOnClickListener { adjustSelected(0.01) } }
    }

    private fun selectCell(index: Int) {
        if (index !in draft.indices) return
        val range = manualVoltageRangeCentivolts(draft, index, manualReferenceCell) ?: return
        selectedCell = index
        cellContainers.forEachIndexed { cellIndex, view -> view.background = ContextCompat.getDrawable(this, if (cellIndex == index) R.drawable.cell_selected else R.drawable.cell_background) }
        editorMinCentivolts = range.first; editorMaxCentivolts = range.last
        val currentCentivolts = ((draft[index].toDoubleOrNull() ?: ((range.first + range.last) / 200.0)) * 100).roundToInt().coerceIn(range.first, range.last)
        findViewById<SeekBar>(R.id.voltageSlider).max = range.last - range.first
        findViewById<SeekBar>(R.id.voltageSlider).isEnabled = true
        findViewById<Button>(R.id.decreaseVoltage).isEnabled = true
        findViewById<Button>(R.id.increaseVoltage).isEnabled = true
        findViewById<TextView>(R.id.measurementError).text = ""
        findViewById<TextView>(R.id.selectedCellTitle).text = String.format(Locale.US, "Комірка %d · Модуль %s\nДіапазон %.2f–%.2f V", index + 1, if (index < 6) "A" else "B", range.first / 100.0, range.last / 100.0)
        findViewById<TextView>(R.id.selectedCellValue).text = draft[index].toDoubleOrNull()?.let { String.format(Locale.US, "%.2f V", it) } ?: "—"
        findViewById<SeekBar>(R.id.voltageSlider).progress = currentCentivolts - range.first
    }

    private fun adjustSelected(delta: Double) {
        if (selectedCell !in draft.indices || editorMaxCentivolts <= editorMinCentivolts) return
        val min = editorMinCentivolts / 100.0; val max = editorMaxCentivolts / 100.0
        val sliderValue = min + findViewById<SeekBar>(R.id.voltageSlider).progress / 100.0
        val current = draft[selectedCell].toDoubleOrNull() ?: sliderValue
        setSelectedVoltage((current + delta).coerceIn(min, max))
    }

    private fun setSelectedVoltage(value: Double) {
        if (selectedCell !in draft.indices) return
        if (manualReferenceCell == null) manualReferenceCell = selectedCell
        draft[selectedCell] = String.format(Locale.US, "%.2f", value)
        if (selectedCell == manualReferenceCell) clampDependentCellVoltages(draft, selectedCell)
        findViewById<TextView>(R.id.selectedCellValue).text = String.format(Locale.US, "%.2f V", value)
        findViewById<SeekBar>(R.id.voltageSlider).progress = ((value * 100).roundToInt() - editorMinCentivolts).coerceIn(0, editorMaxCentivolts - editorMinCentivolts)
        updateSummary()
    }

    private fun requestPreview(values: List<Double>) {
        val item = battery ?: return
        val generation = ++previewGeneration
        networkExecutor.execute {
            runCatching { api.previewMeasurement(item.id, values) }.onSuccess { result -> runOnUiThread {
                if (screen != Screen.MEASUREMENT || generation != previewGeneration) return@runOnUiThread
                previewValid = true
                findViewById<Button>(R.id.saveMeasurementButton).isEnabled = true
                findViewById<TextView>(R.id.measurementError).text = ""
                findViewById<TextView>(R.id.measurementSummary).apply {
                    text = String.format(Locale.US, "Загальна: %.2f V  ·  Min: %.2f V  ·  Max: %.2f V\nDelta: %.2f V  ·  Заряд: %d%%  ·  %s", result.totalVoltage, result.minCellVoltage, result.maxCellVoltage, result.cellDelta, result.chargePercent, healthLabel(result.health))
                    setTextColor(ContextCompat.getColor(this@MainActivity, when (result.health) { "danger" -> R.color.status_danger; "warning" -> R.color.status_warning; else -> R.color.text_primary }))
                }
            } }.onFailure { error -> runOnUiThread {
                if (screen == Screen.MEASUREMENT && generation == previewGeneration) findViewById<TextView>(R.id.measurementError).text = error.message ?: "Не вдалося перевірити вимірювання"
            } }
        }
    }

    private fun healthLabel(health: String) = when (health) { "danger" -> "НЕБЕЗПЕЧНО"; "warning" -> "УВАГА"; else -> "ДОБРЕ" }

    private fun age(iso: String): String = runCatching {
        val minutes = Duration.between(Instant.parse(iso), Instant.now()).toMinutes().coerceAtLeast(0)
        when { minutes < 1 -> "щойно"; minutes < 60 -> "$minutes хв"; minutes < 1440 -> "${minutes / 60} год"; else -> "${minutes / 1440} дн" }
    }.getOrDefault("—")

    private fun showHistory() {
        val item = battery ?: return showBatteries()
        screen = Screen.HISTORY; setScreenContent(R.layout.activity_history)
        findViewById<TextView>(R.id.historyBatteryLabel).text = item.label
        historyOffset = 0; historyLoading = false; historyHasMore = true
        val scroll = findViewById<ScrollView>(R.id.historyScroll)
        scroll.setOnScrollChangeListener { _, _, scrollY, _, _ ->
            val content = scroll.getChildAt(0)
            if (content != null && scrollY + scroll.height >= content.height - dp(160)) loadHistoryPage()
        }
        findViewById<TextView>(R.id.historyStatus).setOnClickListener { if (!historyLoading) loadHistoryPage() }
        loadHistoryPage()
    }

    private fun loadHistoryPage() {
        val item = battery ?: return
        if (screen != Screen.HISTORY || historyLoading || !historyHasMore) return
        historyLoading = true
        findViewById<ProgressBar>(R.id.historyProgress).visibility = View.VISIBLE
        findViewById<TextView>(R.id.historyStatus).text = "Завантаження…"
        val requestedOffset = historyOffset
        networkExecutor.execute {
            runCatching { api.batteryHistory(item.id, requestedOffset) }.onSuccess { page -> runOnUiThread {
                if (screen != Screen.HISTORY || battery?.id != item.id) return@runOnUiThread
                val list = findViewById<LinearLayout>(R.id.historyList)
                page.items.forEach { list.addView(historyCard(it)) }
                historyOffset = page.nextOffset ?: historyOffset
                historyHasMore = page.nextOffset != null
                historyLoading = false
                findViewById<ProgressBar>(R.id.historyProgress).visibility = View.GONE
                findViewById<TextView>(R.id.historyStatus).text = when {
                    list.childCount == 0 -> "Історія поки порожня"
                    historyHasMore -> "Прокрутіть далі для завантаження"
                    else -> "Уся історія завантажена"
                }
                val scroll = findViewById<ScrollView>(R.id.historyScroll)
                scroll.post {
                    if (screen == Screen.HISTORY && historyHasMore && scroll.getChildAt(0)?.height.orZero() <= scroll.height) loadHistoryPage()
                }
            } }.onFailure { error -> runOnUiThread {
                if (screen != Screen.HISTORY) return@runOnUiThread
                historyLoading = false
                findViewById<ProgressBar>(R.id.historyProgress).visibility = View.GONE
                findViewById<TextView>(R.id.historyStatus).text = "${error.message ?: "Не вдалося завантажити"} · натисніть, щоб повторити"
            } }
        }
    }

    private fun historyCard(item: BatteryHistoryItem): View {
        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(15), dp(11), dp(15), dp(11))
            background = ContextCompat.getDrawable(this@MainActivity, R.drawable.cell_background)
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { setMargins(0, 0, 0, dp(8)) }
            isClickable = true; isFocusable = true
        }
        card.addView(TextView(this).apply {
            text = historyTitle(item); textSize = 16f; setTypeface(typeface, Typeface.BOLD)
            setTextColor(ContextCompat.getColor(this@MainActivity, when (item.kind) { "charge" -> R.color.status_good; "discharge" -> R.color.status_warning; else -> R.color.text_primary }))
        })
        card.addView(TextView(this).apply {
            text = formatHistoryTime(item.occurredAt); textSize = 12f
            setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_muted))
        })
        historyDetails(item).takeIf { it.isNotBlank() }?.let { details ->
            card.addView(TextView(this).apply {
                text = details; textSize = 14f; setPadding(0, dp(5), 0, 0)
                setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary))
            })
        }
        val expanded = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL; visibility = View.GONE
            setPadding(0, dp(10), 0, dp(4))
        }
        if (item.kind == "measurement" && !item.cellVoltages.isNullOrEmpty()) {
            expanded.addView(TextView(this).apply {
                text = historyMeasurementStats(item); textSize = 13f
                setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary))
            })
            expanded.addView(historyModuleRow("МОДУЛЬ A", item.cellVoltages.take(6), 0))
            expanded.addView(historyModuleRow("МОДУЛЬ B", item.cellVoltages.drop(6).take(6), 6))
        } else {
            expanded.addView(TextView(this).apply {
                text = historyExpandedDetails(item); textSize = 13f
                setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary))
            })
        }
        card.addView(expanded)
        val hint = TextView(this).apply {
            text = "ДЕТАЛІ  ▾"; textSize = 11f; setPadding(0, dp(7), 0, 0)
            setTextColor(ContextCompat.getColor(this@MainActivity, R.color.lime))
        }
        card.addView(hint)
        card.setOnClickListener {
            val opening = expanded.visibility != View.VISIBLE
            expanded.visibility = if (opening) View.VISIBLE else View.GONE
            hint.text = if (opening) "ЗГОРНУТИ  ▴" else "ДЕТАЛІ  ▾"
        }
        return card
    }

    private fun historyModuleRow(label: String, values: List<Double>, offset: Int): View = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL; setPadding(0, dp(9), 0, 0)
        addView(TextView(this@MainActivity).apply {
            text = label; textSize = 11f; setTypeface(typeface, Typeface.BOLD)
            setTextColor(ContextCompat.getColor(this@MainActivity, R.color.lime))
        })
        addView(LinearLayout(this@MainActivity).apply {
            orientation = LinearLayout.HORIZONTAL
            values.forEachIndexed { index, voltage ->
                addView(LinearLayout(this@MainActivity).apply {
                    orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER
                    background = ContextCompat.getDrawable(this@MainActivity, R.drawable.cell_background)
                    setPadding(dp(2), dp(5), dp(2), dp(5))
                    addView(TextView(this@MainActivity).apply {
                        text = "${offset + index + 1}"; gravity = Gravity.CENTER; textSize = 10f
                        setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_muted))
                    })
                    addView(TextView(this@MainActivity).apply {
                        text = String.format(Locale.US, "%.2f", voltage); gravity = Gravity.CENTER; textSize = 12f; setTypeface(typeface, Typeface.BOLD)
                        setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary))
                    })
                }, LinearLayout.LayoutParams(0, -2, 1f).apply { setMargins(dp(1), 0, dp(1), 0) })
            }
        })
    }

    private fun historyMeasurementStats(item: BatteryHistoryItem): String = buildString {
        append(listOfNotNull(
            item.totalVoltage?.let { String.format(Locale.US, "Загальна: %.2f V", it) },
            item.chargePercent?.let { "Заряд: $it%" },
            item.health?.let { "Стан: ${healthLabel(it)}" }
        ).joinToString(" · "))
        append("\n")
        append(listOfNotNull(
            item.minCellVoltage?.let { String.format(Locale.US, "Min: %.2f V", it) },
            item.maxCellVoltage?.let { String.format(Locale.US, "Max: %.2f V", it) },
            item.cellDelta?.let { String.format(Locale.US, "Δ %.2f V", it) }
        ).joinToString(" · "))
        item.notes?.takeIf { it.isNotBlank() }?.let { append("\nПримітка: $it") }
    }

    private fun historyExpandedDetails(item: BatteryHistoryItem): String = buildString {
        when (item.kind) {
            "charge", "discharge" -> {
                append(if (item.inferred == true) "Джерело: автоматичний аналіз вимірювань" else "Джерело: ручний запис")
                append("\nЗміна циклів: ${item.cycleDelta ?: 0}")
            }
            "transfer" -> append("Звідки: ${item.fromCrewName ?: "без екіпажу"}\nКуди: ${item.toCrewName ?: "невідомий екіпаж"}")
            else -> {
                item.flightMinutes?.let { append("Тривалість польоту: $it хв") }
                if (isEmpty()) append("Додаткових даних немає")
            }
        }
        item.notes?.takeIf { it.isNotBlank() }?.let { append("\nПримітка: $it") }
    }

    private fun historyTitle(item: BatteryHistoryItem) = when (item.kind) {
        "measurement" -> "Вимірювання"
        "charge" -> "Заряд"
        "discharge" -> "Розряд"
        "archive" -> "Призупинення експлуатації (архів)"
        "restore" -> "Відновлення експлуатації"
        "retirement" -> "Виведення з експлуатації (списання)"
        "transfer" -> "Передача"
        else -> "Подія"
    }

    private fun historyDetails(item: BatteryHistoryItem): String = buildString {
        when (item.kind) {
            "measurement" -> append(listOfNotNull(
                item.chargePercent?.let { "$it%" },
                item.totalVoltage?.let { String.format(Locale.US, "%.2f V", it) },
                item.cellDelta?.let { String.format(Locale.US, "Δ %.2f V", it) },
                item.health?.let(::healthLabel)
            ).joinToString(" · "))
            "charge", "discharge" -> {
                if (item.inferred == true) append("Визначено автоматично за вимірюваннями")
                if ((item.cycleDelta ?: 0) > 0) append(if (isEmpty()) "Цикл +${item.cycleDelta}" else " · Цикл +${item.cycleDelta}")
            }
            "transfer" -> append("${item.fromCrewName ?: "Без екіпажу"} → ${item.toCrewName ?: "Екіпаж"}")
            else -> item.flightMinutes?.let { append("Політ: $it хв") }
        }
    }

    private fun formatHistoryTime(value: String): String = runCatching {
        HISTORY_TIME_FORMAT.format(Instant.parse(value).atZone(ZoneId.systemDefault()))
    }.getOrDefault(value)

    private fun setScreenContent(layoutId: Int) {
        setContentView(layoutId)
        val root = findViewById<View>(R.id.screenRoot)
        val left = root.paddingLeft; val top = root.paddingTop; val right = root.paddingRight; val bottom = root.paddingBottom
        ViewCompat.setOnApplyWindowInsetsListener(root) { view, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout())
            view.setPadding(left + bars.left, top + bars.top, right + bars.right, bottom + bars.bottom)
            insets
        }
        ViewCompat.requestApplyInsets(root)
    }

    private fun swipeActionLabel(label: String, labelGravity: Int) = TextView(this).apply {
        text = label; gravity = labelGravity; textSize = 16f; setTypeface(typeface, Typeface.BOLD)
        setTextColor(ContextCompat.getColor(this@MainActivity, R.color.lime))
    }

    private fun openCellEditor(index: Int) {
        if (index !in draft.indices) {
            findViewById<TextView>(R.id.measurementError).text = "Не вдалося відкрити комірку"
            return
        }
        selectCell(index)
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun Int?.orZero() = this ?: 0
    override fun onDestroy() { networkExecutor.shutdown(); super.onDestroy() }

    companion object {
        private val HISTORY_TIME_FORMAT = DateTimeFormatter.ofPattern("dd.MM.yyyy · HH:mm")
    }
}
