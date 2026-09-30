plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

val releaseSigningEnvironment = listOf(
    "ANDROID_KEYSTORE_PATH", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD"
).associateWith { providers.environmentVariable(it).orNull }

val validateReleaseSigning by tasks.registering {
    doLast {
        val missing = releaseSigningEnvironment.filterValues { it.isNullOrBlank() }.keys
        check(missing.isEmpty()) { "Missing Android release signing environment variables: ${missing.joinToString()}" }
        check(file(releaseSigningEnvironment.getValue("ANDROID_KEYSTORE_PATH")!!).isFile) {
            "Android release keystore file does not exist."
        }
    }
}

tasks.matching { it.name == "preReleaseBuild" || it.name == "validateSigningRelease" }.configureEach {
    dependsOn(validateReleaseSigning)
}

android {
    namespace = "com.sarmat.crew"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.sarmat.crew"
        minSdk = 26
        targetSdk = 36
        versionCode = 2
        versionName = "0.2.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        create("release") {
            storeFile = releaseSigningEnvironment["ANDROID_KEYSTORE_PATH"]?.takeIf { it.isNotBlank() }?.let { file(it) }
            storePassword = releaseSigningEnvironment["ANDROID_KEYSTORE_PASSWORD"]
            keyAlias = releaseSigningEnvironment["ANDROID_KEY_ALIAS"]
            keyPassword = releaseSigningEnvironment["ANDROID_KEY_PASSWORD"]
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName("release")
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    testOptions { unitTests.isIncludeAndroidResources = true }
}

dependencies {
    implementation("androidx.core:core-ktx:1.17.0")
    implementation("androidx.appcompat:appcompat:1.8.0")
    implementation("androidx.work:work-runtime-ktx:2.11.2")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.16.1")
    testImplementation("com.squareup.okhttp3:mockwebserver:4.12.0")
}
