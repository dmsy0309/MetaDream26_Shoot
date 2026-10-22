#!/usr/bin/env python3
"""Loopback protocol v10 smoke test against a user-started dedicated server. No external services."""
import socket,struct,time,sys,json
from pathlib import Path
PORT=int(sys.argv[1]) if len(sys.argv)>1 else 18990
HASH=sys.argv[2] if len(sys.argv)>2 else ''
def string(s):
 b=s.encode();n=len(b);v=b''
 while n>=128:v+=bytes([(n&127)|128]);n>>=7
 return v+bytes([n])+b
def readstr(b,i):
 n=0;s=0
 while True:
  v=b[i];i+=1;n|=(v&127)<<s
  if v<128:break
  s+=7
 return b[i:i+n].decode(),i+n
def send(s,t,b=b''):
 b=bytes([t])+b;s.sendall(struct.pack('<i',len(b))+b)
def read(s):
 def exact(n):
  b=b''
  while len(b)<n:
   c=s.recv(n-len(b))
   if not c:raise EOFError()
   b+=c
  return b
 n=struct.unpack('<i',exact(4))[0];return exact(n)
def connect(name,hash):
 s=socket.create_connection(('127.0.0.1',PORT),timeout=5);s.settimeout(5)
 send(s,1,struct.pack('<i',10)+string(hash)+string(name)+string('local-smoke-'+name)+bytes([0,0,0,3,1])+struct.pack('<8f',.1,0,0,.1,0,0,0,0))
 while True:
  p=read(s)
  if p[0]==3:raise RuntimeError(readstr(p,1)[0])
  if p[0]==2:return s,p[1]
a,slot=connect('검증A',HASH);b,slotB=connect('검증B',HASH)
msg='안녕하세요 <b>텍스트</b>'
send(a,11,string(msg))
for s in [a,b]:
 while True:
  p=read(s)
  if p[0]==12:
   name,i=readstr(p,2);text,i=readstr(p,i)
   assert name=='검증A' and text==msg and p[1]==slot
   print(json.dumps({'sender':name,'text':text,'slot':slot,'received':True},ensure_ascii=False));break
send(a,8);send(b,8);a.close();b.close()
