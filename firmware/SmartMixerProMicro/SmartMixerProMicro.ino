#include <Arduino.h>
#include <string.h>

static const uint8_t CHANNELS = 7;
static const uint8_t POT_PINS[CHANNELS] = {A0, A1, A2, A3, A6, A7, A8};
static const uint8_t LED_PINS[CHANNELS] = {2, 3, 5, 7, 14, 15, 16};
static const uint8_t TOUCH_PIN = 9;
static const bool TOUCH_ACTIVE_LOW = false;
static const uint32_t BAUD_RATE = 115200;
static const uint16_t REPORT_INTERVAL_MS = 30;
static const uint16_t TOUCH_DEBOUNCE_MS = 40;
static const uint8_t LINE_CAPACITY = 64;

static uint16_t readings[CHANNELS];
static uint32_t packetSequence = 0;
static uint32_t lastReportAt = 0;
static uint8_t ledMask = 0;
static bool firefoxMode = false;
static bool stableTouch = false;
static bool lastTouchRead = false;
static uint32_t touchChangedAt = 0;
static uint32_t testUntil[CHANNELS] = {0};
static char commandBuffer[LINE_CAPACITY];
static uint8_t commandLength = 0;

static void writeLedState() {
  const uint32_t now = millis();
  for (uint8_t i = 0; i < CHANNELS; i++) {
    const bool testActive = testUntil[i] != 0 && (int32_t)(testUntil[i] - now) > 0;
    if (testUntil[i] != 0 && !testActive) testUntil[i] = 0;
    const bool assigned = (ledMask & (1U << i)) != 0;
    const bool modePulse = firefoxMode && assigned && ((now / 250U) % 2U == 0U);
    digitalWrite(LED_PINS[i], testActive || (assigned && !modePulse) ? HIGH : LOW);
  }
}

static bool parseUnsigned(const char *text, uint16_t *value, uint16_t limit) {
  if (text == nullptr || *text == '\0') return false;
  uint32_t parsed = 0;
  for (const char *p = text; *p != '\0'; ++p) {
    if (*p < '0' || *p > '9') return false;
    parsed = parsed * 10U + (uint32_t)(*p - '0');
    if (parsed > limit) return false;
  }
  *value = (uint16_t)parsed;
  return true;
}

static void processCommand(char *line) {
  char *save = nullptr;
  char *kind = strtok_r(line, ",", &save);
  if (kind == nullptr) return;

  if (strcmp(kind, "V") == 0) {
    if (strtok_r(nullptr, ",", &save) != nullptr) return;
    Serial.println(F("I,1.0.0"));
    return;
  }

  if (strcmp(kind, "L") == 0) {
    uint16_t mask = 0;
    uint16_t mode = 0;
    if (!parseUnsigned(strtok_r(nullptr, ",", &save), &mask, 127) ||
        !parseUnsigned(strtok_r(nullptr, ",", &save), &mode, 1) ||
        strtok_r(nullptr, ",", &save) != nullptr) return;
    ledMask = (uint8_t)mask;
    firefoxMode = mode == 1;
    writeLedState();
    return;
  }

  if (strcmp(kind, "T") == 0) {
    uint16_t channel = 0;
    uint16_t duration = 0;
    if (!parseUnsigned(strtok_r(nullptr, ",", &save), &channel, CHANNELS) ||
        channel < 1 ||
        !parseUnsigned(strtok_r(nullptr, ",", &save), &duration, 5000) ||
        duration < 50 ||
        strtok_r(nullptr, ",", &save) != nullptr) return;
    testUntil[channel - 1] = millis() + duration;
    writeLedState();
    return;
  }

  if (strcmp(kind, "P") == 0) {
    char *sequence = strtok_r(nullptr, ",", &save);
    uint16_t parsedSequence = 0;
    if (!parseUnsigned(sequence, &parsedSequence, 65535) ||
        strtok_r(nullptr, ",", &save) != nullptr) return;
    Serial.print(F("A,"));
    Serial.println(parsedSequence);
  }
}

static void readCommands() {
  while (Serial.available() > 0) {
    const char value = (char)Serial.read();
    if (value == '\n') {
      commandBuffer[commandLength] = '\0';
      processCommand(commandBuffer);
      commandLength = 0;
    } else if (value != '\r') {
      if (commandLength + 1 < LINE_CAPACITY) {
        commandBuffer[commandLength++] = value;
      } else {
        commandLength = 0;
      }
    }
  }
}

static bool readTouch() {
  const bool rawTouch = digitalRead(TOUCH_PIN) == (TOUCH_ACTIVE_LOW ? LOW : HIGH);
  const uint32_t now = millis();
  if (rawTouch != lastTouchRead) {
    lastTouchRead = rawTouch;
    touchChangedAt = now;
  }
  if (rawTouch != stableTouch && (uint32_t)(now - touchChangedAt) >= TOUCH_DEBOUNCE_MS)
    stableTouch = rawTouch;
  return stableTouch;
}

static void sendTelemetry(bool touch) {
  Serial.print(F("D,"));
  Serial.print(packetSequence++);
  for (uint8_t i = 0; i < CHANNELS; i++) {
    Serial.print(',');
    Serial.print(readings[i]);
  }
  Serial.print(',');
  Serial.println(touch ? 1 : 0);
}

void setup() {
  Serial.begin(BAUD_RATE);
  pinMode(TOUCH_PIN, TOUCH_ACTIVE_LOW ? INPUT_PULLUP : INPUT);
  for (uint8_t i = 0; i < CHANNELS; i++) {
    pinMode(LED_PINS[i], OUTPUT);
    digitalWrite(LED_PINS[i], LOW);
  }
  Serial.println(F("I,1.0.0"));
}

void loop() {
  readCommands();
  writeLedState();
  const uint32_t now = millis();
  if ((uint32_t)(now - lastReportAt) >= REPORT_INTERVAL_MS) {
    lastReportAt = now;
    for (uint8_t i = 0; i < CHANNELS; i++)
      readings[i] = (uint16_t)analogRead(POT_PINS[i]);
    sendTelemetry(readTouch());
  }
}
