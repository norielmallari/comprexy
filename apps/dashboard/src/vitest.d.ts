/// <reference types="vitest/globals" />
/// <reference types="@testing-library/jest-dom" />

// Custom JSX elements used in test mocks
declare namespace JSX {
  interface IntrinsicElements {
    'mock-bar': React.DetailedHTMLProps<React.HTMLAttributes<HTMLElement>, HTMLElement>;
  }
}
