"""Install the Brands Hatch boundary data used by the wheel-limits pilot."""

from pathlib import Path
import re
from urllib.request import urlopen
import xml.etree.ElementTree as ET

SOURCE = (
    "https://raw.githubusercontent.com/SpeedHQ/RaceIQ/"
    "930f65425e9a0c271a6612107deec2f3fec01b3d/"
    "shared/data/tracks/acc/brands-hatch.track.svg"
)
TARGET = Path(__file__).resolve().parents[1] / "bin" / "TrackLimits" / "brands_hatch.csv"


def sample_ring(path_data):
    commands = re.findall(r"[A-Za-z]", path_data)
    numbers = list(map(float, re.findall(r"-?\d+(?:\.\d+)?", path_data)))
    if (
        len(commands) < 5
        or commands[0] != "M"
        or commands[-1] != "Z"
        or any(command != "C" for command in commands[1:-1])
        or len(numbers) != 2 + 6 * (len(commands) - 2)
    ):
        raise ValueError("Expected a closed ring made of cubic Bezier segments.")

    x, z = numbers[:2]
    points = [(x, z)]
    for offset in range(2, len(numbers), 6):
        a, b, c, d, end_x, end_z = numbers[offset:offset + 6]
        for step in range(1, 9):
            t = step / 8
            u = 1 - t
            points.append((
                u**3 * x + 3 * u * u * t * a + 3 * u * t * t * c + t**3 * end_x,
                u**3 * z + 3 * u * u * t * b + 3 * u * t * t * d + t**3 * end_z,
            ))
        x, z = end_x, end_z
    return points


def main():
    with urlopen(SOURCE, timeout=20) as response:
        svg = ET.fromstring(response.read())
    paths = {
        path.get("class"): path.attrib["d"]
        for path in svg.findall("{*}path")
        if path.get("class") in ("track-limit left", "track-limit right")
    }
    rings = [sample_ring(paths["track-limit " + side]) for side in ("left", "right")]
    csv = "\n\n".join("\n".join(f"{x:.5f},{z:.5f}" for x, z in ring) for ring in rings)
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_bytes((csv + "\n").encode("ascii"))
    print(f"Installed {sum(map(len, rings))} boundary points at {TARGET}")


if __name__ == "__main__":
    main()
