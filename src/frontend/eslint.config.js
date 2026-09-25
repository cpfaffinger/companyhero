// Lint-Grenzen aus den Integrationsregeln (concept/architektur-integrationsregeln.md, Abschnitt 6):
// keine konkurrierenden UI-Pakete, keine Palettenableitung im Frontend, keine Rohfarben in Komponenten,
// keine manuell geänderten generierten Clients (der generierte Ordner ist vom Lint ausgenommen und wird in CI regeneriert).
// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

/** Pakete, die neben Angular Material nicht eingesetzt werden (A-003). */
const forbiddenUiPackages = [
  '@ng-bootstrap/*', 'bootstrap', 'bootstrap/*', 'primeng', 'primeng/*', 'primeicons', '@ionic/*',
  'tailwindcss', 'tailwindcss/*', 'ng-zorro-antd', 'ng-zorro-antd/*', '@nebular/*', '@clr/*', '@taiga-ui/*',
  '@ng-select/*', 'ngx-bootstrap', 'ngx-bootstrap/*', '@angular/material-experimental', '@angular/material-experimental/*',
];

/** Kein zweites State- oder Query-Framework (A-004, K11). */
const forbiddenStatePackages = ['@ngrx/*', '@ngxs/*', '@tanstack/*', 'rxjs-state', 'akita', '@datorama/*', 'elf', '@ngneat/elf*'];

/** Keine Palettenableitung und keine Kontrastprüfung im Frontend (A-013, K01). */
const forbiddenColorPackages = [
  '@material/material-color-utilities', 'culori', 'culori/*', 'chroma-js', 'color', 'colord', 'tinycolor2',
  'polished', 'd3-color', 'color-convert', 'wcag-contrast', 'apca-w3',
];

module.exports = tseslint.config(
  {
    ignores: ['dist/**', '.angular/**', 'node_modules/**', 'src/libs/api-client/generated/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'ch', style: 'camelCase' }],
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: 'ch', style: 'kebab-case' }],
      '@angular-eslint/prefer-standalone': 'error',
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            { group: forbiddenUiPackages, message: 'Angular Material ist die alleinige UI-Bibliothek (A-003).' },
            { group: forbiddenStatePackages, message: 'Kein zusätzliches State- oder Query-Framework (A-004, K11).' },
            { group: forbiddenColorPackages, message: 'Palettenableitung und Kontrastprüfung erfolgen im Backend (A-013, K01).' },
            { group: ['@angular/forms'], importNames: ['FormsModule', 'NgModel'], message: 'Typed Reactive Forms besitzen den Formularzustand (K10).' },
          ],
        },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: 'Literal[value=/^#[0-9a-fA-F]{3,8}$/]',
          message: 'Keine Rohfarben im Frontend; Farben kommen als --ch-* aus dem Backend-Tokensatz (A-076, K01).',
        },
        {
          selector: 'Literal[value=/^(rgb|rgba|hsl|hsla|oklch|oklab)\\(/]',
          message: 'Keine Rohfarben im Frontend; Farben kommen als --ch-* aus dem Backend-Tokensatz (A-076, K01).',
        },
        {
          selector: 'TemplateElement[value.raw=/#[0-9a-fA-F]{6}\\b/]',
          message: 'Keine Rohfarben in Vorlagen oder Inline-Styles (A-076, K01).',
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {
      '@angular-eslint/template/no-inline-styles': ['error', { allowNgStyle: false, allowBindToStyle: false }],
    },
  },
);
