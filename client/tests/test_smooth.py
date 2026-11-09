"""The graph interval follows whatever gaps the samples actually have."""

from __future__ import annotations

import unittest

from xeneon.smooth import median_interval, sample_curve


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


if __name__ == "__main__":
    unittest.main()
