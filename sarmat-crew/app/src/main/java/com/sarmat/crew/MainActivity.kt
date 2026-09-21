package com.sarmat.crew

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.os.Bundle
import android.text.InputType
import android.util.Size
import android.view.Gravity
import android.view.View
import android.widget.*
import androidx.activity.OnBackPressedCallback
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.camera.core.*
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import androidx.core.widget.doAfterTextChanged
import com.sarmat.crew.api.ApiException
import com.sarmat.crew.api.BatterySummary
import com.sarmat.crew.api.CrewApi
import com.sarmat.crew.scanner.LcdScanner
import com.sarmat.crew.scanner.ScanAccumulator
import com.sarmat.crew.scanner.ScanQuad
import com.sarmat.crew.scanner.StableScan
import com.sarmat.crew.ui.ScannerOverlayView
import org.opencv.android.OpenCVLoader
import org.opencv.core.Core
import org.opencv.core.CvType
import org.opencv.core.Mat
import java.util.Locale
import java.util.concurrent.Executors

class MainActivity : AppCompatActivity() {
    private enum class Screen { LOGIN, BATTERIES, MEASUREMENT, SCANNER }

    private lateinit var api: CrewApi
    private val cameraExecutor = Executors.newSingleThreadExecutor()
    private val networkExecutor = Executors.newSingleThreadExecutor()
    private lateinit var scanner: LcdScanner
    private var provider: ProcessCameraProvider? = null
    private var screen = Screen.LOGIN
    private var battery: BatterySummary? = null
    private var draft = mutableListOf<String>()
    private var draftNotes = ""
    private val inputs = mutableListOf<EditText>()
    private var moduleOffset = 0
    private var recognized: List<Double>? = null

    private lateinit var preview: PreviewView
    private lateinit var overlay: ScannerOverlayView
    private lateinit var status: TextView
    private lateinit var useButton: Button
    private val scanValues = mutableListOf<TextView>()
    private var accumulator = ScanAccumulator()
    private var missingFrames = 0

    private val cameraPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted && screen == Screen.SCANNER) startCamera()
        else if (screen == Screen.SCANNER) status.text = getString(R.string.camera_permission)
    }

    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        api = CrewApi(getSharedPreferences("sarmat_crew", MODE_PRIVATE))
        if (OpenCVLoader.initLocal()) scanner = LcdScanner()
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                when (screen) {
                    Screen.SCANNER -> showMeasurement()
                    Screen.MEASUREMENT -> showBatteries()
                    else -> { isEnabled = false; onBackPressedDispatcher.onBackPressed() }
                }
            }
        })
        if (api.hasSession()) showBatteries() else showLogin()
    }

    private fun showLogin(message: String = "") {
        stopCamera(); screen = Screen.LOGIN; setContentView(R.layout.activity_login)
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
        stopCamera(); screen = Screen.BATTERIES; setContentView(R.layout.activity_batteries)
        findViewById<TextView>(R.id.crewNameText).text = api.savedCrewLabel()
        findViewById<Button>(R.id.refreshButton).setOnClickListener { loadBatteries() }
        findViewById<Button>(R.id.logoutButton).setOnClickListener {
            networkExecutor.execute { api.logout(); runOnUiThread { showLogin() } }
        }
        loadBatteries()
    }

    private fun loadBatteries() {
        val statusView = findViewById<TextView>(R.id.listStatusText).apply { text = getString(R.string.loading) }
        val list = findViewById<LinearLayout>(R.id.batteryList).apply { removeAllViews() }
        networkExecutor.execute {
            runCatching(api::batteries).onSuccess { items -> runOnUiThread {
                if (screen != Screen.BATTERIES) return@runOnUiThread
                statusView.text = if (items.isEmpty()) "У екіпажу немає активних батарей" else "${items.size} батарей"
                items.forEach { list.addView(batteryCard(it)) }
            } }.onFailure { failure -> runOnUiThread {
                if (failure is ApiException && failure.status == 401) showLogin("Сесія завершилась. Увійдіть знову.")
                else statusView.text = failure.message ?: "Не вдалося завантажити батареї"
            } }
        }
    }

    private fun batteryCard(item: BatterySummary): View = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL; setPadding(dp(18), dp(15), dp(18), dp(15))
        background = ContextCompat.getDrawable(this@MainActivity, R.drawable.cell_background)
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { setMargins(0, 0, 0, dp(10)) }
        addView(TextView(this@MainActivity).apply {
            text = item.label; textSize = 20f; setTypeface(typeface, Typeface.BOLD)
            setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary))
        })
        val latest = item.latestTotalVoltage?.let { total ->
            String.format(Locale.US, "%.2f V%s%s", total, item.latestChargePercent?.let { " · $it%" } ?: "", item.latestDelta?.let { String.format(Locale.US, " · Δ %.2f V", it) } ?: "")
        } ?: "Ще не перевірялась"
        addView(TextView(this@MainActivity).apply {
            text = "${item.serialNumber} · ${item.typeName} · ${item.cellCount}S\n$latest"
            setTextColor(ContextCompat.getColor(this@MainActivity, when (item.latestHealth) { "danger" -> R.color.scanner_red; "warning" -> R.color.scanner_yellow; else -> R.color.text_muted }))
        })
        setOnClickListener { battery = item; draft = MutableList(item.cellCount) { "" }; draftNotes = ""; showMeasurement() }
    }

    private fun showMeasurement() {
        stopCamera(); val item = battery ?: return showBatteries()
        screen = Screen.MEASUREMENT; setContentView(R.layout.activity_measurement)
        findViewById<TextView>(R.id.batteryTitle).text = item.label
        findViewById<TextView>(R.id.batteryMeta).text = "${item.serialNumber} · ${item.typeName} · ${item.capacityAh} Ah · ${item.cellCount}S"
        findViewById<Button>(R.id.backToBatteriesButton).setOnClickListener { showBatteries() }
        findViewById<Button>(R.id.scanAButton).apply {
            text = getString(R.string.scan_module, "A"); visibility = if (item.cellCount == 12) View.VISIBLE else View.GONE
            setOnClickListener { keepDraft(); showScanner(0) }
        }
        findViewById<Button>(R.id.scanBButton).apply {
            text = getString(R.string.scan_module, "B"); visibility = if (item.cellCount == 12) View.VISIBLE else View.GONE
            setOnClickListener { keepDraft(); showScanner(6) }
        }
        createInputs(item.cellCount)
        findViewById<EditText>(R.id.notesInput).setText(draftNotes)
        findViewById<Button>(R.id.saveMeasurementButton).setOnClickListener { saveMeasurement() }
    }

    private fun createInputs(count: Int) {
        val container = findViewById<LinearLayout>(R.id.measurementCells); inputs.clear()
        draft = MutableList(count) { draft.getOrNull(it).orEmpty() }
        for (start in 0 until count step 6) {
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            for (index in start until minOf(start + 6, count)) {
                val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER; layoutParams = LinearLayout.LayoutParams(0, -2, 1f) }
                box.addView(TextView(this).apply { text = "${index + 1}"; gravity = Gravity.CENTER; setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_muted)) })
                val input = EditText(this).apply {
                    setText(draft[index]); gravity = Gravity.CENTER; inputType = InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_DECIMAL
                    setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary)); doAfterTextChanged { updateSummary() }
                }
                box.addView(input, LinearLayout.LayoutParams(-1, dp(52))); row.addView(box); inputs += input
            }
            container.addView(row)
        }
        updateSummary()
    }

    private fun updateSummary() {
        if (screen != Screen.MEASUREMENT || inputs.isEmpty()) return
        val values = inputs.mapNotNull { it.text.toString().toDoubleOrNull() }
        findViewById<TextView>(R.id.measurementSummary).text = if (values.size == inputs.size)
            String.format(Locale.US, "Total %.2f V · Δ %.2f V", values.sum(), values.max() - values.min())
        else "Заповнено ${values.size} із ${inputs.size} комірок"
    }

    private fun keepDraft() {
        draft = inputs.map { it.text.toString() }.toMutableList()
        draftNotes = findViewById<EditText>(R.id.notesInput).text.toString()
    }

    private fun saveMeasurement() {
        val item = battery ?: return
        val error = findViewById<TextView>(R.id.measurementError); val button = findViewById<Button>(R.id.saveMeasurementButton)
        val nullable = inputs.map { it.text.toString().toDoubleOrNull() }
        if (nullable.any { it == null }) { error.text = "Заповніть усі ${item.cellCount} комірок"; return }
        val cells = nullable.filterNotNull(); val min = item.minVoltage / item.cellCount; val max = item.maxVoltage / item.cellCount
        if (cells.any { it !in min..max }) { error.text = String.format(Locale.US, "Допустимий діапазон: %.2f–%.2f V", min, max); return }
        button.isEnabled = false; error.text = "Зберігаю…"
        val notes = findViewById<EditText>(R.id.notesInput).text.toString()
        networkExecutor.execute {
            runCatching { api.saveMeasurement(item.id, cells, notes) }.onSuccess { runOnUiThread(::showBatteries) }
                .onFailure { runOnUiThread { error.text = it.message ?: "Не вдалося зберегти"; button.isEnabled = true } }
        }
    }

    private fun showScanner(offset: Int) {
        if (!::scanner.isInitialized) return
        screen = Screen.SCANNER; moduleOffset = offset; recognized = null; accumulator = ScanAccumulator(); missingFrames = 0
        setContentView(R.layout.activity_scanner)
        preview = findViewById(R.id.preview); overlay = findViewById(R.id.scannerOverlay); status = findViewById(R.id.statusText); useButton = findViewById(R.id.useValuesButton)
        findViewById<TextView>(R.id.scannerTitle).text = "БАТАРЕЯ ${if (offset == 0) "A" else "B"}"
        findViewById<Button>(R.id.cancelScannerButton).setOnClickListener { showMeasurement() }
        useButton.setOnClickListener {
            recognized?.forEachIndexed { index, value -> draft[moduleOffset + index] = String.format(Locale.US, "%.2f", value) }
            showMeasurement()
        }
        scanValues.clear(); createScanValues(findViewById(R.id.cellValues))
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) startCamera() else cameraPermission.launch(Manifest.permission.CAMERA)
    }

    private fun startCamera() {
        val future = ProcessCameraProvider.getInstance(this)
        future.addListener({
            if (screen != Screen.SCANNER) return@addListener
            try {
                val current = future.get().also { provider = it }; val rotation = preview.display?.rotation ?: android.view.Surface.ROTATION_0
                val cameraPreview = Preview.Builder().setTargetRotation(rotation).build().also { it.surfaceProvider = preview.surfaceProvider }
                val analysis = ImageAnalysis.Builder().setTargetRotation(rotation).setTargetResolution(Size(1280, 720)).setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).build().also { it.setAnalyzer(cameraExecutor, ::analyze) }
                current.unbindAll(); current.bindToLifecycle(this, CameraSelector.DEFAULT_BACK_CAMERA, cameraPreview, analysis)
            } catch (_: Exception) { status.text = getString(R.string.camera_error) }
        }, ContextCompat.getMainExecutor(this))
    }

    private fun analyze(image: ImageProxy) {
        try {
            val source = imageToGray(image); val upright = rotate(source, image.imageInfo.rotationDegrees); if (upright !== source) source.release()
            val frame = scanner.scan(upright); upright.release()
            if (frame.displayFound) missingFrames = 0 else if (++missingFrames >= 12) { accumulator.clear(); missingFrames = 0 }
            val stable = accumulator.add(frame)
            runOnUiThread {
                if (screen == Screen.SCANNER) renderScan(stable, frame.debugRows, frame.sourceWidth, frame.sourceHeight)
            }
        } catch (_: Exception) {} finally { image.close() }
    }

    private fun imageToGray(image: ImageProxy): Mat {
        val plane = image.planes[0]; val mat = Mat(image.height, image.width, CvType.CV_8UC1); val bytes = ByteArray(image.width * image.height); val buffer = plane.buffer.apply { rewind() }
        if (plane.pixelStride == 1 && plane.rowStride == image.width) buffer.get(bytes, 0, minOf(bytes.size, buffer.remaining())) else {
            val row = ByteArray(plane.rowStride); var target = 0
            repeat(image.height) { val length = minOf(plane.rowStride, buffer.remaining()); buffer.get(row, 0, length); repeat(image.width) { x -> bytes[target++] = row[x * plane.pixelStride] } }
        }
        mat.put(0, 0, bytes); return mat
    }

    private fun rotate(source: Mat, degrees: Int): Mat {
        if (degrees == 0) return source; val result = Mat()
        when (degrees) { 90 -> Core.rotate(source, result, Core.ROTATE_90_CLOCKWISE); 180 -> Core.rotate(source, result, Core.ROTATE_180); 270 -> Core.rotate(source, result, Core.ROTATE_90_COUNTERCLOCKWISE); else -> return source }
        return result
    }

    private fun renderScan(scan: StableScan, rows: List<ScanQuad>, sourceWidth: Int, sourceHeight: Int) {
        scan.cells.forEachIndexed { index, reading -> scanValues[index].text = reading?.let { String.format(Locale.US, "%.2f", it.value) } ?: "—" }
        val state = when { !scan.displayFound -> ScannerOverlayView.State.SEARCHING; scan.isComplete -> ScannerOverlayView.State.RECOGNIZED; else -> ScannerOverlayView.State.READING }
        overlay.setState(state)
        overlay.setDetectedRows(rows, sourceWidth, sourceHeight)
        status.text = getString(when (state) { ScannerOverlayView.State.SEARCHING -> R.string.state_searching; ScannerOverlayView.State.READING -> R.string.state_reading; ScannerOverlayView.State.RECOGNIZED -> R.string.state_recognized })
        if (scan.isComplete) recognized = scan.cells.map { it!!.value }; useButton.isEnabled = scan.isComplete
    }

    private fun createScanValues(container: LinearLayout) = repeat(6) { index ->
        val value = TextView(this).apply { text = "—"; gravity = Gravity.CENTER; textSize = 18f; setTypeface(typeface, Typeface.BOLD); setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary)) }
        container.addView(LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER; background = ContextCompat.getDrawable(this@MainActivity, R.drawable.cell_background); layoutParams = LinearLayout.LayoutParams(0, -1, 1f).apply { setMargins(dp(3), 0, dp(3), 0) }
            addView(TextView(this@MainActivity).apply { text = "${index + 1}"; gravity = Gravity.CENTER; setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_muted)) }); addView(value)
        }); scanValues += value
    }

    private fun stopCamera() { provider?.unbindAll(); provider = null }
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    override fun onDestroy() { stopCamera(); if (::scanner.isInitialized) scanner.close(); cameraExecutor.shutdown(); networkExecutor.shutdown(); super.onDestroy() }
}
