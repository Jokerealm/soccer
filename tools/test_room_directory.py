#!/usr/bin/env python3
"""Exercise the real room directory, including session ownership, collisions and expiring leases."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json,re,socket,subprocess,time,uuid
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request,urlopen

root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--url');p.add_argument('--start-server',action='store_true');p.add_argument('--lease-seconds',type=int,default=3);p.add_argument('--report',type=Path,default=root/'reports/room-codes-019-directory.json');args=p.parse_args()
server=None;log=None;checks=[]
def check(name,condition):
 checks.append({'name':name,'passed':bool(condition)});print(('PASS ' if condition else 'FAIL ')+name,flush=True)
def call(method,path,body=None,token=None):
 headers={'Content-Type':'application/json'}
 if token:headers['Authorization']='Bearer '+token
 req=Request(args.url+path,data=None if body is None else json.dumps(body).encode(),method=method,headers=headers)
 try:
  with urlopen(req,timeout=6) as response:raw=response.read();return response.status,json.loads(raw) if raw else {}
 except HTTPError as error:
  raw=error.read()
  try:body=json.loads(raw) if raw else {}
  except ValueError:body={}
  return error.code,body
def publication(code='AB12CD',**changes):
 data={'code':code,'roomId':uuid.uuid4().hex,'name':'目录验证房间','host':'','port':32123,'players':1,'running':False,'requiresPassword':True};data.update(changes);return data
def login(account):
 status,data=call('POST','/api/login',{'account':account,'password':account});check('network login issues a room-service session for '+account,status==200 and bool(re.fullmatch('[A-F0-9]{64}',data.get('accessToken',''))));return data['accessToken']
try:
 if args.start_server:
  with socket.socket() as reservation:reservation.bind(('127.0.0.1',0));port=reservation.getsockname()[1]
  args.url='http://127.0.0.1:'+str(port);args.report.parent.mkdir(parents=True,exist_ok=True);log=open(args.report.with_suffix('.log'),'w')
  server=subprocess.Popen([str(root/'tools/dotnet'),'run','--no-build','--project',str(root/'server/Soccer.LoginServer'),'--','--urls',args.url,'--RoomDirectory:LeaseSeconds='+str(args.lease_seconds)],cwd=root,stdout=log,stderr=subprocess.STDOUT)
  deadline=time.monotonic()+15
  while time.monotonic()<deadline:
   try:
    if call('GET','/health')[0]==200:break
   except OSError:pass
   if server.poll() is not None:raise RuntimeError('test service failed to start')
   time.sleep(.1)
  else:raise RuntimeError('test service startup timed out')
 if not args.url:raise RuntimeError('provide --url or --start-server')
 owner=login('a');guest=login('b');same_account=login('a');check('separate logins of one debug account have distinct session ownership',owner!=same_account)
 check('anonymous room publication is rejected',call('POST','/api/rooms',publication())[0]==401)
 check('anonymous room lookup is rejected',call('GET','/api/rooms/AB12CD')[0]==401)
 check('invalid session token is rejected',call('POST','/api/rooms',publication(),token='0'*64)[0]==401)
 for changes in [{'code':'ABC12'},{'code':'ABCDEF'},{'code':'123456'},{'roomId':'bad'},{'port':0},{'players':7},{'host':'user@host'},{'name':''}]:
  check('invalid publication field '+next(iter(changes))+' is rejected',call('POST','/api/rooms',publication(**changes),owner)[0]==400)
 data=publication();status,room=call('POST','/api/rooms',data,owner);code=room.get('code','');check('publication creates a six-character mixed room code',status==201 and bool(re.fullmatch('[A-Z0-9]{6}',code)) and any(c.isdigit() for c in code) and any(c.isalpha() for c in code))
 check('service determines host IP and preserves the program-selected port',room.get('host')=='127.0.0.1' and room.get('port')==32123)
 duplicate={**data,'code':'XY34ZZ'};status,again=call('POST','/api/rooms',duplicate,owner);check('retry is idempotent for the same host session and room identity',status==200 and again.get('code')==code)
 with ThreadPoolExecutor(max_workers=12) as pool:
  competing=list(pool.map(lambda _:call('POST','/api/rooms',publication(code=code),owner),range(12)))
 other_codes=[body.get('code') for status,body in competing]
 check('concurrent preferred-code collisions receive distinct room codes',all(status==201 for status,_ in competing) and len(set([code]+other_codes))==13)
 status,resolved=call('GET','/api/rooms/'+code.lower(),token=guest);check('guest resolves lowercase input to the actual room endpoint',status==200 and resolved.get('roomId')==data['roomId'] and resolved.get('code')==code)
 check('directory publishes password requirement without accepting or returning passwords',resolved.get('requiresPassword') is True and 'password' not in resolved and 'accessToken' not in resolved)
 check('another session of the same shared account cannot overwrite the room',call('PUT','/api/rooms/'+code,data,same_account)[0]==403)
 check('another account cannot remove the room',call('DELETE','/api/rooms/'+code,token=guest)[0]==403)
 check('invalid room-code lookup format is rejected',call('GET','/api/rooms/AB-12C',token=guest)[0]==400)
 check('unknown room code returns a clear miss',call('GET','/api/rooms/ZZZZZZ',token=guest)[0]==404)
 check('renewal cannot change room identity',call('PUT','/api/rooms/'+code,publication(code=code),owner)[0]==400)
 data['code']=code;data['running']=True;call('PUT','/api/rooms/'+code,data,owner)
 status,body=call('GET','/api/rooms/'+code,token=guest);check('started match refuses new room-code joins',status==409 and '开始' in body.get('error',''))
 data['running']=False;data['players']=6;call('PUT','/api/rooms/'+code,data,owner)
 status,body=call('GET','/api/rooms/'+code,token=guest);check('full room refuses new room-code joins',status==409 and '已满' in body.get('error',''))
 data['players']=2;data['port']=32124;status,body=call('PUT','/api/rooms/'+code,data,owner)
 check('host heartbeat updates port and player count',status==200 and body.get('port')==32124 and body.get('players')==2)
 time.sleep(args.lease_seconds*.6);check('host renews the lease before it expires',call('PUT','/api/rooms/'+code,data,owner)[0]==200)
 time.sleep(args.lease_seconds*.6);check('renewed room survives the original expiry time',call('GET','/api/rooms/'+code,token=guest)[0]==200)
 time.sleep(args.lease_seconds*.6);check('lost host automatically expires without a close request',call('GET','/api/rooms/'+code,token=guest)[0]==404)
 status,new=call('POST','/api/rooms',publication(code=code),owner);check('recently expired code is not immediately assigned to a different room',status==201 and new.get('code')!=code)
 new_code=new['code'];check('host explicitly closes its published room',call('DELETE','/api/rooms/'+new_code,token=owner)[0]==204)
 check('closed room can no longer be resolved',call('GET','/api/rooms/'+new_code,token=guest)[0]==404)
 status,third=call('POST','/api/rooms',publication(code=new_code),owner);check('recently closed code is reserved against stale-number reuse',status==201 and third.get('code')!=new_code)
 call('DELETE','/api/rooms/'+third['code'],token=owner)
finally:
 if server is not None and server.poll() is None:
  server.terminate()
  try:server.wait(8)
  except subprocess.TimeoutExpired:server.kill();server.wait()
 if log:log.close()
 if checks:
  args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps({'passed':all(c['passed'] for c in checks),'total':len(checks),'checks':checks,'leaseSeconds':args.lease_seconds},ensure_ascii=False,indent=2)+'\n')
raise SystemExit(0 if checks and all(c['passed'] for c in checks) else 1)
