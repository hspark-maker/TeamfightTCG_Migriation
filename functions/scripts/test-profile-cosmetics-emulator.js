"use strict";
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) throw new Error("Local emulator + demo project required");
const {db} = require("../lib/firebaseApp");
const {mutateSave} = require("../lib/save/saveDocument");
const {grantRewardItems} = require("../lib/rewards/itemGrant");
const {ensureProfileCosmetics} = require("../lib/commands/ensureProfileCosmetics");
const specs = require("../lib/specs/specBlobReader");
const {parseCosmeticItems, assertCosmeticPurchasable} = require("../lib/profile/cosmetics");
const {nextWallet} = require("../lib/currency/walletStore");
const rows = [{id:1,itemType:"Avatar",itemId:"base",defaultOwned:1},
 {id:2,itemType:"Avatar",itemId:"a",defaultOwned:0},{id:3,itemType:"Frame",itemId:"f",defaultOwned:0}];
const cosmetics = parseCosmeticItems(rows);
const context = {cosmetics,catalog:new Set(),grades:new Map(),thresholds:[],packs:new Map(),choices:[],cards:[]};
after(()=>db.terminate());
async function setup() {
 const uid = "cosmetics-"+randomUUID();const root=db.doc("envs/test/users/"+uid);
 await root.collection("save").doc("current").set({schemaVersion:8,revision:1,profile:{nickname:"kept",avatarId:"base",accountExp:9}});
 await root.collection("wallet").doc("current").set({rev:1,balances:{Gold:100},paidBalances:{}});
 return {uid,root,read:async()=> (await root.collection("save").doc("current").get()).data()};
}
async function purchase(uid,type,id,txId,fail=false) {
 let award;
 return mutateSave("test",uid,"cosmeticTestPurchase",{kind:"client",txId},(current,tx,wallet)=>{
   assertCosmeticPurchasable(current.profile,type,id,cosmetics);
   award=grantRewardItems(current,[{rewardType:type,rewardId:id,amount:1}],context,[],"",0);
   if(fail)throw new Error("simulated transaction failure");
   return {slots:award.slots,wallet:nextWallet(wallet,{...wallet.balances,Gold:wallet.balances.Gold-10},"cosmeticTestPurchase")};
 },adopted=>({...adopted,cosmetics:award.cosmetics}));
}
test("concurrent same receipt debits once, replay precedes already-owned rejection",async()=>{
 const {uid,root,read}=await setup();const txId=randomUUID();
 const both=await Promise.all([purchase(uid,"Avatar","a",txId),purchase(uid,"Avatar","a",txId)]);
 assert.deepEqual(both[0].cosmetics,both[1].cosmetics);
 assert.deepEqual((await read()).profile.ownedAvatarIds,["base","a"]);
 assert.equal((await read()).revision,2);
 assert.equal((await root.collection("wallet").doc("current").get()).data().balances.Gold,90);
 await assert.rejects(purchase(uid,"Avatar","a",randomUUID()));
 assert.equal((await root.collection("wallet").doc("current").get()).data().balances.Gold,90);
});
test("concurrent different grants preserve both, failure writes neither ownership nor charge",async()=>{
 const {uid,root,read}=await setup();
 await assert.rejects(purchase(uid,"Avatar","a",randomUUID(),true));
 assert.equal((await read()).revision,1);
 assert.equal((await root.collection("wallet").doc("current").get()).data().balances.Gold,100);
 await Promise.all([purchase(uid,"Avatar","a",randomUUID()),purchase(uid,"Frame","f",randomUUID())]);
 const save=await read();assert.deepEqual(save.profile.ownedAvatarIds,["base","a"]);
 assert.deepEqual(save.profile.ownedFrameIds,["f"]);assert.equal(save.profile.accountExp,9);
 assert.equal((await root.collection("wallet").doc("current").get()).data().balances.Gold,80);
});
test("legacy ensure is transactional and idempotent without dropping other profile fields",async t=>{
 t.mock.method(specs,"readSpecRows",async()=>rows);
 const {uid,read}=await setup(); const input={auth:{uid},data:{env:"test"}};
 const first=await ensureProfileCosmetics.run(input);const second=await ensureProfileCosmetics.run(input);
 assert.equal(first.changed,true);assert.equal(second.changed,false);assert.equal(second.revision,first.revision);
 assert.equal((await read()).profile.nickname,"kept");assert.equal((await read()).profile.avatarId,"base");
});
