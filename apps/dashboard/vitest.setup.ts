import "@testing-library/jest-dom";

// Mock matchMedia (used by some UI libraries)
Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: vi.fn().mockImplementation((query) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});

// Mock localStorage
const localStorageMock = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: vi.fn((key: string) => store[key] || null),
    setItem: vi.fn((key: string, value: string) => {
      store[key] = value;
    }),
    removeItem: vi.fn((key: string) => {
      delete store[key];
    }),
    clear: vi.fn(() => {
      store = {};
    }),
  };
})();

Object.defineProperty(window, "localStorage", {
  value: localStorageMock,
});

// Mock Next.js Image component
vi.mock("next/image", () => ({
  default: (props: any) => {
    const { src, alt, ...rest } = props;
    return `<img src="${src}" alt="${alt}" ${Object.keys(rest).map((key) => `${key}="${rest[key]}"`).join(" ")} />`;
  },
}));

// Mock Next.js Font objects
vi.mock("next/font/local", () => ({
  __esModule: true,
  default: () => ({ className: "font-mock" }),
}));

vi.mock("next/font/google", () => ({
  __esModule: true,
  default: () => ({ className: "font-mock" }),
}));
