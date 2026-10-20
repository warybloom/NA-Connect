const assert = require('node:assert/strict');
const fs = require('node:fs');
const page = fs.readFileSync('src/features/planning/ProjectCostsPage.tsx','utf8');
assert.match(page, /api\/project-costs\/summary/);
assert.match(page, /api\/project-costs\/professionals/);
assert.match(page, /"recognition"/);
assert.match(page, /Custos do Projeto/);
assert.match(page, /excedentes/);
console.log('project-costs-contract: 5 checks passed');
