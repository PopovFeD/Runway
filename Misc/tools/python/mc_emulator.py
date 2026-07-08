import argparse
import random
import struct
import sys
import time
from dataclasses import dataclass

try:
    import serial
except ModuleNotFoundError:
    serial = None


MAGIC = 0xAA55
VERSION = 1

TYPE_SENSOR = 0x01


@dataclass(slots=True)
class SensorData:
    temperature: float
    humidity: float


def crc16(data: bytes) -> int:
    crc = 0xFFFF

    for b in data:
        crc ^= b
        for _ in range(8):
            if crc & 1:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1

    return crc & 0xFFFF


def encode_sensor(data: SensorData) -> bytes:
    return struct.pack(
        "<hh", int(round(data.temperature * 100)), int(round(data.humidity * 100))
    )


def encode_packet(packet_type: int, sequence: int, payload: bytes) -> bytes:

    header = struct.pack("<HBBHH", MAGIC, VERSION, packet_type, sequence, len(payload))

    crc = crc16(header + payload)

    return header + payload + struct.pack("<H", crc)


def parse_args():
    parser = argparse.ArgumentParser()

    parser.add_argument("--port", default="COM4")
    parser.add_argument("--baud", type=int, default=115200)
    parser.add_argument("--period", type=float, default=1.0)

    return parser.parse_args()


def main():
    args = parse_args()

    temperature = 24.0
    humidity = 60.0
    sequence = 0

    def emit_packet(
        sequence_value: int, temperature_value: float, humidity_value: float
    ) -> bytes:
        sensor = SensorData(temperature_value, humidity_value)
        payload = encode_sensor(sensor)
        packet = encode_packet(TYPE_SENSOR, sequence_value, payload)
        print(
            f"SEQ={sequence_value:5d}  T={temperature_value:6.2f}  H={humidity_value:6.2f}  BYTES={len(packet)}"
        )
        return packet

    if serial is None:
        print("pyserial is not installed; running in fallback mode.", file=sys.stderr)
        while True:
            temperature += random.uniform(-0.15, 0.15)
            humidity += random.uniform(-0.40, 0.40)
            emit_packet(sequence, temperature, humidity)
            sequence = (sequence + 1) & 0xFFFF
            time.sleep(args.period)

    try:
        with serial.Serial(args.port, args.baud) as port:
            print(f"Connected: {args.port}")

            while True:
                temperature += random.uniform(-0.15, 0.15)
                humidity += random.uniform(-0.40, 0.40)
                packet = emit_packet(sequence, temperature, humidity)
                port.write(packet)
                sequence = (sequence + 1) & 0xFFFF
                time.sleep(args.period)
    except Exception as exc:
        print(
            f"Serial port {args.port} is unavailable: {exc}. Falling back to console output.",
            file=sys.stderr,
        )
        while True:
            temperature += random.uniform(-0.15, 0.15)
            humidity += random.uniform(-0.40, 0.40)
            emit_packet(sequence, temperature, humidity)
            sequence = (sequence + 1) & 0xFFFF
            time.sleep(args.period)


if __name__ == "__main__":
    main()
