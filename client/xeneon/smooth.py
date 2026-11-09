"""Graph timing and a curve that stays inside the real samples.

The host poll can be 0.5 seconds or 10 seconds or anything else. The interval
is the rolling median of the gaps between sample timestamps, so a change in
the host setting shows up after a few new samples. The curve is a monotone
cubic: it bends, and it does not rise above or fall below the samples it joins.
"""

from __future__ import annotations

import math


def median_interval(timestamps: list[float], window: int = 7) -> float:
    """Seconds between samples. Uses the latest gaps so a new rate replaces the old one.

    A single gap is enough. No gaps means one second, which is only a stand-in
    until the second sample arrives.
    """
    gaps = [right - left for left, right in zip(timestamps, timestamps[1:]) if right > left]
    if not gaps:
        return 1.0
    recent = sorted(gaps[-max(1, window):])
    count = len(recent)
    middle = count // 2
    if count % 2:
        return recent[middle]
    return (recent[middle - 1] + recent[middle]) / 2


def _slopes(xs: list[float], ys: list[float]) -> list[float]:
    """Fritsch-Carlson slopes. A zero or a turn gets a flat tangent, so the arc cannot overshoot."""
    count = len(xs)
    if count < 2:
        return [0.0] * count
    delta = [(ys[index + 1] - ys[index]) / (xs[index + 1] - xs[index]) for index in range(count - 1)]
    slope = [0.0] * count
    slope[0] = delta[0]
    slope[-1] = delta[-1]
    for index in range(1, count - 1):
        if delta[index - 1] == 0 or delta[index] == 0 or delta[index - 1] * delta[index] < 0:
            slope[index] = 0.0
        else:
            slope[index] = (delta[index - 1] + delta[index]) / 2
    for index in range(count - 1):
        if delta[index] == 0:
            slope[index] = 0.0
            slope[index + 1] = 0.0
            continue
        alpha = slope[index] / delta[index]
        beta = slope[index + 1] / delta[index]
        hyp = alpha * alpha + beta * beta
        if hyp > 9:
            scale = 3 / math.sqrt(hyp)
            slope[index] = scale * alpha * delta[index]
            slope[index + 1] = scale * beta * delta[index]
    return slope


def sample_curve(xs: list[float], ys: list[float], steps: int = 8) -> list[float]:
    """Y values along the curve, including the real samples. Each span stays between its two ends."""
    if len(xs) != len(ys) or len(xs) == 0:
        return []
    if len(xs) == 1:
        return [ys[0]]
    slope = _slopes(xs, ys)
    out: list[float] = []
    for index in range(len(xs) - 1):
        span = xs[index + 1] - xs[index]
        low = min(ys[index], ys[index + 1])
        high = max(ys[index], ys[index + 1])
        for step in range(steps):
            unit = step / steps
            unit2 = unit * unit
            unit3 = unit2 * unit
            y = (
                (2 * unit3 - 3 * unit2 + 1) * ys[index]
                + (unit3 - 2 * unit2 + unit) * span * slope[index]
                + (-2 * unit3 + 3 * unit2) * ys[index + 1]
                + (unit3 - unit2) * span * slope[index + 1]
            )
            out.append(min(high, max(low, y)))
    out.append(ys[-1])
    return out
