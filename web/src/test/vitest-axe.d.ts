import 'vitest';

// vitest-axe ships matcher types for older Vitest namespaces; declare the matcher for the current API.
declare module 'vitest' {
  interface Assertion<T> {
    toHaveNoViolations(): T;
  }
  interface AsymmetricMatchersContaining {
    toHaveNoViolations(): void;
  }
}
