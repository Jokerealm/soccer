#!/usr/bin/env python3
"""Exercise real account social HTTP endpoints and restart persistence."""
import argparse,json,os,subprocess,time
from pathlib import Path
from urllib.request import Request,urlopen
from urllib.error import HTTPError
p=argparse.ArgumentParser();p.add_argument('--report',type=Path,default=Path('reports/social-025-service.json'));p.add_argument('--url',default='http://127.0.0.1:27863');args=p.parse_args()
root=Path(__file__).resolve().parents[1];args.report.parent.mkdir(parents=True,exist_ok=True);save=args.report.parent/'social-state'/'service-tests.json'
save.parent.mkdir(exist_ok=True)
if save.exists():save.unlink()
env=os.environ.copy();env['Social__DataPath']=str(save.resolve());checks=[];process=None;log=None
def start():
 global process,log
 log=open(args.report.with_suffix('.log'),'a');process=subprocess.Popen([str(root/'tools/dotnet'),'run','--no-build','--project',str(root/'server/Soccer.LoginServer'),'--urls',args.url],stdout=log,stderr=subprocess.STDOUT,env=env,cwd=root)
 for _ in range(100):
  try:
   with urlopen(args.url+'/health',timeout=1) as r:
    if r.status==200:return
  except Exception:pass
  if process.poll() is not None:raise RuntimeError('test server failed')
  time.sleep(.1)
 raise RuntimeError('test server startup timed out')
def stop():
 if process is not None and process.poll() is None:process.terminate();process.wait(10)
 if log is not None:log.close()
def call(path,account=None,method='GET',data=None):
 headers={'Content-Type':'application/json'}
 if account:headers['Authorization']='Bearer '+tokens[account]
 req=Request(args.url+path,data=None if data is None else json.dumps(data).encode(),headers=headers,method=method)
 try:
  with urlopen(req,timeout=5) as r:return r.status,json.loads(r.read())
 except HTTPError as e:
  try:body=json.loads(e.read())
  except ValueError:body={}
  return e.code,body
def check(name,value):
 checks.append({'name':name,'passed':bool(value)});print(('PASS ' if value else 'FAIL ')+name,flush=True)
def login():
 return {a:call('/api/login',method='POST',data={'account':a,'password':a})[1]['accessToken'] for a in ['a','b']}
try:
 start();tokens=login()
 check('social endpoints require a valid account session',call('/api/friends')[0]==401 and call('/api/profiles/a')[0]==401)
 check('unknown account search returns no fabricated player',call('/api/profiles/missing','a')[0]==404)
 check('self request is rejected',call('/api/friends/a/request','a','POST',{})[0]==400)
 check('unrequested acceptance is rejected',call('/api/friends/b/accept','a','POST',{})[0]==404)
 status,outgoing=call('/api/friends/b/request','a','POST',{});check('request appears in sender outbox',status==200 and outgoing['Outgoing'][0]['AccountId']=='b')
 status,duplicate=call('/api/friends/b/request','a','POST',{});check('duplicate friend request is idempotent',status==200 and len(duplicate['Outgoing'])==1)
 check('recipient receives actual sender identity',call('/api/friends','b')[1]['Incoming'][0]['AccountId']=='a')
 check('reverse request does not silently accept',call('/api/friends/a/request','b','POST',{})[0]==409)
 call('/api/friends/a/reject','b','POST',{});check('rejection clears both pending views',call('/api/friends','a')[1]['Outgoing']==[] and call('/api/friends','b')[1]['Incoming']==[])
 call('/api/friends/b/request','a','POST',{});call('/api/friends/a/accept','b','POST',{});check('acceptance creates bilateral friendship',call('/api/friends','a')[1]['Friends'][0]['AccountId']=='b' and call('/api/friends','b')[1]['Friends'][0]['AccountId']=='a')
 check('existing friends cannot send another request',call('/api/friends/b/request','a','POST',{})[0]==409)
 match={'MatchId':'fixture-1','FinishedUnixSeconds':int(time.time()),'Source':0,'Result':0,'Character':3,'OwnScore':2,'OpponentScore':1,'Goals':2,'Knockouts':3,'Coins':100,'DurationSeconds':175}
 check('finished match uploads under the authenticated account',call('/api/profile/matches','a','POST',match)[0]==200)
 check('identical completed match upload is idempotent',call('/api/profile/matches','a','POST',match)[0]==200)
 profile=call('/api/profiles/a','b')[1];check('friend sees actual career and recent details',profile['Heroes'][0]['Matches']==1 and profile['Heroes'][0]['Wins']==1 and profile['Recent'][0]['Goals']==2)
 changed=dict(match,Goals=9);check('same match id cannot rewrite prior results',call('/api/profile/matches','a','POST',changed)[0]==409)
 check('client cannot attribute match stats to a different account',call('/api/profiles/b','a')[1]['Heroes'][0]['Matches']==0)
 for i in range(2,56):
  item=dict(match,MatchId='fixture-'+str(i),FinishedUnixSeconds=match['FinishedUnixSeconds']-56+i);assert call('/api/profile/matches','a','POST',item)[0]==200
 profile=call('/api/profiles/a','b')[1];check('career survives the recent fifty cap',len(profile['Recent'])==50 and profile['Heroes'][0]['Matches']==55)
 check('retired recent match ids remain deduplicated',call('/api/profile/matches','a','POST',dict(match,MatchId='fixture-2',FinishedUnixSeconds=match['FinishedUnixSeconds']-54))[0]==200 and call('/api/profiles/a','a')[1]['Heroes'][0]['Matches']==55)
 stop();start();tokens=login();check('friendship survives server restart',call('/api/friends','b')[1]['Friends'][0]['AccountId']=='a')
 profile=call('/api/profiles/a','b')[1];check('career and recent stats survive server restart',profile['Heroes'][0]['Matches']==55 and len(profile['Recent'])==50)
 call('/api/friends/b','a','DELETE');check('removing friendship is bilateral',call('/api/friends','a')[1]['Friends']==[] and call('/api/friends','b')[1]['Friends']==[])
 invalid=dict(match,MatchId='invalid',DurationSeconds=-1);check('invalid statistics are rejected without changing career',call('/api/profile/matches','a','POST',invalid)[0]==400 and call('/api/profiles/a','a')[1]['Heroes'][0]['Matches']==55)
finally:stop()
summary={'passed':all(c['passed'] for c in checks),'total':len(checks),'checks':checks};args.report.write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n');raise SystemExit(0 if summary['passed'] else 1)
