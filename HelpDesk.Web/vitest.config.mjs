// Keep jsdom/Angular suites sequential to avoid worker contention and timeouts.
// Assertions, isolation, and Vitest's default timeouts remain unchanged.
export default {
  test: {
    fileParallelism: false,
  },
};
