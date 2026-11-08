"""Temperature display bands shared by the CPU and GPU cards.

The bands are display colors, not fan-control or safety limits.
Rising through 65, 75, and 85 moves green -> yellow -> orange -> red.
Cooling through 60, 70, and 80 moves back. One sample may cross several
bounds. There is no dwell time. The 150 ms fade is CSS in the page, not here.
"""

from __future__ import annotations

LEVELS = ("green", "yellow", "orange", "red")
DEFAULT_RISE = (65.0, 75.0, 85.0)
DEFAULT_FALL = (60.0, 70.0, 80.0)


def initial_level(temp: float, rise: tuple[float, float, float] = DEFAULT_RISE) -> str:
    """Pick a band when there is no previous sample.

    Uses only the rising bounds, as if the sensor warmed from cold.
    64.9 is green, 65 is yellow, 75 is orange, 85 is red.
    """
    level = 0
    for bound in rise:
        if temp >= bound:
            level += 1
        else:
            break
    return LEVELS[level]


def next_level(
    previous: str | None,
    temp: float,
    rise: tuple[float, float, float] = DEFAULT_RISE,
    fall: tuple[float, float, float] = DEFAULT_FALL,
) -> str:
    """Return the band after one raw sample.

    previous is the last displayed band, or None for the first sample.
    temp is degrees Celsius from the host, not a smoothed value.
    A jump from green at 50 to 90 becomes red in this one call.
    A jump from red at 90 to 50 becomes green in this one call.
    """
    if previous is None:
        return initial_level(temp, rise)
    if previous not in LEVELS:
        raise ValueError(f"unknown level {previous}")
    index = LEVELS.index(previous)
    while index < 3 and temp >= rise[index]:
        index += 1
    while index > 0 and temp <= fall[index - 1]:
        index -= 1
    return LEVELS[index]
