declare module '*.css';

/** Compile-time flag, substituted by esbuild. False in a host build, which drops the mock. */
declare const __MOCK__: boolean;
