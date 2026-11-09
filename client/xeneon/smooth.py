"""Graph timing.

The host poll can be 0.5 seconds or 10 seconds or anything else. The interval
is the rolling median of the gaps between sample timestamps, so a change in
the host setting shows up after a few new samples.

The page draws a gliding head: when a reading arrives, the displayed value
eases from where it is to that reading over one interval (smoothstep). A gap
holds the last value. A burst of readings glides once, to the newest. The
monotone cubic below is kept for tests of a curve that stays inside samples.
The dashboard does not use it. A spline through old points does not move
between polls, which is why the Pi graph looked stepped.
"""

from __future__ import annotations

import math


def smoothstep(unit: float) -> float:
    """Ease-in-out. 0 and 1 stay put. The middle is slower than a straight line."""
    if unit <= 0:
        return 0.0
    if unit >= 1:
        return 1.0
    return unit * unit * (3.0 - 2.0 * unit)


def glide_value(origin: float, target: float, elapsed: float, duration: float) -> float:
    """Head position. It starts at origin when the reading arrives and reaches target after duration.

    A late reading holds at the last target. Nothing is invented for a gap.
    """
    if duration <= 0:
        return target
    return origin + (target - origin) * smoothstep(elapsed / duration)


class GlideHead:
    """One temperature head. A burst of readings becomes one glide to the newest value."""

    def __init__(self) -> None:
        self.origin: float | None = None
        self.target: float | None = None
        self.started = 0.0
        self.duration = 1.0
        self.seen_t: float | None = None

    def note(self, samples: list[tuple[float, float]], now: float, interval: float) -> None:
        if not samples:
            return
        newest_t, newest_v = samples[-1]
        if self.seen_t is None:
            self.origin = newest_v
            self.target = newest_v
            self.started = now
            self.duration = interval if interval > 0 else 1.0
            self.seen_t = newest_t
            return
        if newest_t <= self.seen_t + 1e-9:
            return
        # Do not replay a backlog. The lag stays about one interval.
        self.origin = self.value(now)
        self.target = newest_v
        self.started = now
        self.duration = interval if interval > 0 else 1.0
        self.seen_t = newest_t

    def value(self, now: float) -> float | None:
        if self.target is None or self.origin is None:
            return None
        return glide_value(self.origin, self.target, now - self.started, self.duration)


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
