import argparse
import sys
import time
import serial


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Send text to a serial port")
    parser.add_argument("--port", default="COM4", help="COM port name")
    parser.add_argument("--baud", type=int, default=115200, help="Baud rate")
    parser.add_argument("--message", default="Hello from Runway", help="Text to send")
    parser.add_argument(
        "--delay", type=float, default=1.0, help="Delay between sends in seconds"
    )
    parser.add_argument("--count", type=int, default=1, help="How many times to send")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    payload = args.message.encode("utf-8") + b"\n"

    try:
        with serial.Serial(args.port, args.baud, timeout=1) as port:
            print(f"Connected to {args.port} at {args.baud} baud")
            for index in range(1, args.count + 1):
                port.write(payload)
                print(f"Sent {index}/{args.count}: {args.message}")
                if index < args.count:
                    time.sleep(args.delay)
    except serial.SerialException as exc:
        print(f"Failed to open {args.port}: {exc}", file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        print("\nStopped by user")


if __name__ == "__main__":
    main()
