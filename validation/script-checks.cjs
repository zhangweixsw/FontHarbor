const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const code=fs.readFileSync(__dirname+'/apply-script.js','utf8');
const font={name:'ArialMT',family:'Arial',style:'Regular'};
function run(app){return vm.runInNewContext(code,{app});}
function app(selection,fonts=[font]){return {documents:[{}],activeDocument:{selection},textFonts:fonts,redraw(){}};}
function char(old={name:'OldFont'}){return {characterAttributes:{textFont:old}};}
assert.match(run({documents:[]}),/^ERROR\|/);
assert.match(run(app([])),/^ERROR\|/);
assert.match(run(app([],[])),/^ERROR\|/);
let c=char(),range={typename:'TextRange',characters:[c]};
assert.equal(run(app(range)),'OK|1');assert.equal(c.characterAttributes.textFont,font);
c=char();assert.equal(run(app([{typename:'GroupItem',pageItems:[{typename:'TextFrame',textRange:{characters:[c]}}]}])),'OK|1');assert.equal(c.characterAttributes.textFont,font);
assert.match(run(app([{typename:'TextFrame',locked:true,textRange:range}])),/^ERROR\|/);
const old={name:'Before'},a=char(old),b={characterAttributes:{}};
Object.defineProperty(b.characterAttributes,'textFont',{get(){return old},set(){throw Error('cannot edit')}});
assert.match(run(app({typename:'TextRange',characters:[a,b]})),/^ERROR\|/);assert.equal(a.characterAttributes.textFont,old);
assert.match(run(app({typename:'TextRange',characters:[]})),/^ERROR\|/);
console.log('PASS: Illustrator script checks: no document, no font, no selection, text selection, grouped frames, locked objects, rollback, empty range.');
