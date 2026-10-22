#!/usr/bin/env python3
"""Repeatable local validation/build ladder; call only with the target Unity project closed."""
from pathlib import Path
import subprocess,sys,xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]
UNITY='/Applications/Unity/Hub/Editor/6000.6.6f1/Unity.app/Contents/MacOS/Unity'
EDITOR='--editor' in sys.argv
PROJECT=ROOT/'MapEditorProject' if EDITOR else ROOT
LOGS=PROJECT/'Logs';LOGS.mkdir(exist_ok=True)
def run(name,extra,graphics=False):
 log=LOGS/('release-'+name+'.log')
 cmd=[UNITY,'-batchmode','-projectPath',str(PROJECT),'-logFile',str(log)]
 if not graphics:cmd+=['-nographics']
 print('START',name,flush=True)
 p=subprocess.run(cmd+extra,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 text=log.read_text(errors='replace') if log.exists() else ''
 if p.returncode or 'error CS' in text or 'result=Failed' in text:
  print('FAILED',name,p.returncode,str(log),flush=True);sys.exit(1)
 print('OK',name,str(log),flush=True)
def tests(mode):
 xml=LOGS/('release-'+mode+'.xml')
 run(mode,['-runTests','-testPlatform',mode,'-testResults',str(xml)],mode=='PlayMode')
 r=ET.parse(xml).getroot();print('TESTS',mode,r.attrib,flush=True)
 if r.get('result')!='Passed' or int(r.get('total','0'))==0:sys.exit(1)
if EDITOR:
 run('verify',['-quit','-executeMethod','Metadream.MapEditor.EditorTools.GameSync.Verify','-gameProject',str(ROOT)])
 tests('EditMode');tests('PlayMode')
 for target,method in [('OSXUniversal','BuildMac'),('Win64','BuildWindows')]:
  run(method,['-quit','-buildTarget',target,'-executeMethod','Metadream.MapEditor.EditorTools.MapEditorSetup.'+method])
else:
 tests('EditMode')
 if '--playmode' in sys.argv:tests('PlayMode')
 for target,method in [('OSXUniversal','BuildMac'),('Win64','BuildWindows'),('Linux64','BuildServerLinux'),('Win64','BuildServerWindows')]:
  run(method,['-quit','-buildTarget',target,'-executeMethod','Metadream.Shoot.EditorTools.ProjectSetup.'+method])
 run('restore-player',['-quit','-buildTarget','OSXUniversal','-standaloneBuildSubtarget','Player'])
