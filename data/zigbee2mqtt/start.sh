#!/bin/bash

# Handle device mounting and configuration
if [ -n "$ZIGBEE_DEVICE_PATH" ] && [ -e "$ZIGBEE_DEVICE_PATH" ]; then
    echo "Zigbee device found: $ZIGBEE_DEVICE_PATH"
    # Device exists, use it
    DEVICE_PORT="$ZIGBEE_DEVICE_PATH"
else
    echo "No zigbee device specified or device not found, using dummy device"
    # Create dummy device for zigbee2mqtt to start
    DEVICE_PORT="/dev/null"
    if [ ! -e /dev/ttyUSB0 ]; then
        mknod /dev/ttyUSB0 c 188 0 2>/dev/null || true
    fi
fi

# Update configuration file with the device path
sed -i "s|\${ZIGBEE_DEVICE_PATH:-/dev/ttyUSB0}|$DEVICE_PORT|g" /app/data/configuration.yaml

# Start zigbee2mqtt
exec /app/index.js
