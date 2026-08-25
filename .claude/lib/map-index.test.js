#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const { mapSymbols, mapTokens } = require("./map-index.js");

const map = [
  "## UI (`UI/`)",
  "- 효과: `UiConfettiBurst.Settings` · `UI/Battle/CardView.cs.PlayDeathAnim`",
].join("\n");
const index = mapSymbols(map);

assert.deepEqual(mapTokens(map), ["UI/", "UiConfettiBurst.Settings", "UI/Battle/CardView.cs.PlayDeathAnim"]);
for (const symbol of ["UiConfettiBurst", "Settings", "CardView", "PlayDeathAnim"]) {
  assert.ok(index.types.has(symbol), `${symbol} 수록`);
}
assert.ok(index.dirs.has("UI/"));
assert.ok(index.members.get("UiConfettiBurst").has("Settings"));
console.log("map-index tests: nested type/member indexing passed");
