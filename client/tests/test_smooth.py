"""The graph interval follows whatever gaps the samples actually have."""

from __future__ import annotations

import unittest

from xeneon.smooth import GlideHead, glide_value, median_interval, sample_curve, smoothstep


class IntervalTests(unittest.TestCase):
    def test_several_rates_and_a_change(self):
        self.assertAlmostEqual(0.5, median_interval([0, 0.5, 1.0, 1.5, 2.0]))
        self.assertAlmostEqual(3, median_interval([0, 3, 6, 9, 12]))
        self.assertAlmostEqual(10, median_interval([0, 10, 20, 30, 40]))
        self.assertAlmostEqual(1, median_interval([100]))
        # A fast run, then the host is set to 10 seconds. The recent gaps win.
        changed = [0, 0.5, 1.0, 1.5, 11.5, 21.5, 31.5, 41.5]
        self.assertAlmostEqual(10, median_interval(changed))
        # Back to half a second. The old 10 second gaps fall out of the window.
        fast_again = changed + [42.0, 42.5, 43.0, 43.5, 44.0, 44.5, 45.0]
        self.assertAlmostEqual(0.5, median_interval(fast_again))

    def test_curve_stays_between_the_real_samples(self):
        xs = [0, 1, 2, 3]
        ys = [40, 70, 42, 55]
        curve = sample_curve(xs, ys, steps=12)
        self.assertEqual(ys[0], curve[0])
        self.assertEqual(ys[-1], curve[-1])
        for index in range(len(xs) - 1):
            span = curve[index * 12:(index + 1) * 12]
            low = min(ys[index], ys[index + 1])
            high = max(ys[index], ys[index + 1])
            self.assertGreaterEqual(min(span), low)
            self.assertLessEqual(max(span), high)
            # A bend exists. The line is not only the two endpoints.
            self.assertGreater(len(set(round(value, 3) for value in span)), 2)

    def test_glide_matches_at_one_three_and_five_seconds(self):
        self.assertEqual(0.0, smoothstep(0))
        self.assertEqual(1.0, smoothstep(1))
        self.assertLess(smoothstep(0.25), 0.25)
        for interval in (1.0, 3.0, 5.0):
            self.assertAlmostEqual(40.0, glide_value(40, 70, 0, interval))
            self.assertAlmostEqual(70.0, glide_value(40, 70, interval, interval))
            self.assertAlmostEqual(70.0, glide_value(40, 70, interval + 2, interval))
            quarter = glide_value(40, 70, interval / 4, interval)
            self.assertAlmostEqual(40 + 30 * smoothstep(0.25), quarter)
            self.assertLess(quarter, 40 + 30 * 0.25)

    def test_a_bunched_backlog_glides_once_to_the_newest(self):
        head = GlideHead()
        head.note([(0.0, 40.0)], 0.0, 3.0)
        head.note([(0.0, 40.0), (3.0, 50.0), (3.1, 80.0)], 3.1, 3.0)
        self.assertAlmostEqual(40.0, head.value(3.1))
        self.assertAlmostEqual(80.0, head.value(6.1))
        # No new reading: hold. Do not invent a slope.
        head.note([(0.0, 40.0), (3.0, 50.0), (3.1, 80.0)], 9.0, 3.0)
        self.assertAlmostEqual(80.0, head.value(9.0))


if __name__ == "__main__":
    unittest.main()
