/** @type {import('jest').Config} */
module.exports = {
  preset: 'jest-preset-angular',
  setupFiles: ['<rootDir>/setup-jest.ts'],
  testPathIgnorePatterns: ['<rootDir>/node_modules/', '<rootDir>/dist/'],
  moduleNameMapper: {
    '^src/(.*)$': '<rootDir>/src/$1',
  },
  transformIgnorePatterns: ['node_modules/(?!.*\\.mjs$)'],
  moduleFileExtensions: ['ts', 'html', 'js', 'json', 'mjs'],
};
