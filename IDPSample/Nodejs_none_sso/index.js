require('dotenv').config();
const express = require('express');
const session = require('express-session');
const { Issuer, generators } = require('openid-client');
const signupRoute = require('./routes/signup');
const userRoute = require('./routes/user');

const app = express();
const port = 5002;

// IMPORTANT: Your REDIRECT_URI must use the SAME host you visit this app with (e.g.,
// http://localhost:5002/oidc/callback). Avoid mixing 127.0.0.1 vs localhost vs IP/hostname,
// otherwise the session cookie won't be sent back and PKCE verifier will be missing.

app.use(express.json()); // ✅ JSON body 파싱용
app.use(express.urlencoded({ extended: true }));
app.set('trust proxy', 1); // if behind proxy/https, keeps cookies working
app.use(session({
  secret: 'my-secret-key',
  resave: false,
  saveUninitialized: false, // don't create empty sessions
  cookie: {
    sameSite: 'lax',
    secure: false // set true only when serving this app over HTTPS
  }
}));
//SSO 미들웨어
app.use((req, res, next) => {
  const excludedPaths = [
    '/oidc/login',
    '/oidc/callback',
    '/oidc/logout',
    '/oidc/home',
    '/auth/signup',
    '/favicon.ico',
    '/backchannel_logout' // 백채널 로그아웃 엔드포인트 제외
  ];

  const isExcluded = excludedPaths.some(path => req.path.startsWith(path));
  if (isExcluded) {
    console.log(`✅ 제외된 경로: ${req.path}`);
    return next();
  }

  console.log(`🔍 보호된 경로 접근 시도: ${req.path}`);

  if (req.session.claims) {
    console.log('🔑 세션에 claims 존재:', req.session.claims);
    return next();
  } else {
    console.log('❌ 세션 claims 없음. 로그인 필요');
    return res.redirect('/oidc/login');
  }
});

app.use('/auth', signupRoute);
app.use('/users', userRoute);

let client;
let codeVerifier;

// OIDC 초기화
async function initOIDC() {
  console.log('🔍 OIDC_ISSUER_URL:', process.env.OIDC_ISSUER_URL);

  const oidcIssuer = await Issuer.discover(process.env.OIDC_ISSUER_URL);
  console.log('✅ OIDC Issuer discovered:', oidcIssuer.issuer);

  client = new oidcIssuer.Client({
    client_id: process.env.OIDC_CLIENT_ID,
    //client_secret: process.env.OIDC_CLIENT_SECRET,
    token_endpoint_auth_method: 'none', // CLIENT_SECRET 없이 인증
    redirect_uris: [process.env.REDIRECT_URI],
    post_logout_redirect_uris: [process.env.POST_LOGOUT_REDIRECT_URI],
    response_types: ['code'],
  });
}

initOIDC();

// 로그인 요청
app.get('/oidc/login', (req, res) => {
  const codeVerifier  = generators.codeVerifier();
  const codeChallenge = generators.codeChallenge(codeVerifier);

  req.session.loginMode = (req.query.mode === 'popup') ? 'popup' : 'redirect';
  req.session.codeVerifier = codeVerifier;

  const authUrl = client.authorizationUrl({
    scope: 'openid profile email',
    code_challenge: codeChallenge,
    code_challenge_method: 'S256'
  });

  return res.redirect(authUrl);
});
// ====== 콜백(팝업/일반 병합 + userinfo 조회) ======
app.get('/oidc/callback', async (req, res) => {
  try {
    const params = client.callbackParams(req);
    const tokenSet = await client.callback(
      process.env.REDIRECT_URI,
      params,
      { code_verifier: req.session.codeVerifier }
    );

    // 표준 클레임
    const claims = tokenSet.claims();

    // 🔎 userinfo 엔드포인트로 상세 프로필 조회 (access_token 필요)
    let userinfo = {};
    try {
      userinfo = await client.userinfo(tokenSet);
    } catch (e) {
      console.warn('userinfo 호출 실패(무시 가능):', e.message);
    }

    // 세션 저장
    req.session.tokenSet = {
      access_token: tokenSet.access_token,
      id_token: tokenSet.id_token,
      expires_at: tokenSet.expires_at,
      scope: tokenSet.scope
      // refresh_token은 필요 시 저장 (보안 유의)
      // refresh_token: tokenSet.refresh_token,
    };
    req.session.claims = claims;
    req.session.userinfo = userinfo;

    const mode = req.session.loginMode || 'redirect';

    
    if (mode === 'popup') {
      // ✅ 세션 저장 완료를 보장한 뒤 postMessage
      req.session.save(() => {
        res.type('html').send(`<!doctype html>
          <meta charset="utf-8">
          <script>
            (function() {
              try {
                if (window.opener) {
                  window.opener.postMessage({ type: 'oidc:done' }, '${ORIGIN}');
                }
              } catch (e) {}
              window.close();
            })();
          </script>
          로그인 성공. 창을 닫습니다.`);
      });
    } else {
      res.redirect('/oidc/home');
    }
  } catch (err) {
    console.error('❌ 콜백 오류:', err);
    res.status(400).send('Callback error');
  }
});

// 로그아웃 처리
app.get('/oidc/logout', (req, res) => {
  const logoutUrl = client.endSessionUrl({
    id_token_hint: req.session.tokenSet?.id_token,
    post_logout_redirect_uri: process.env.POST_LOGOUT_REDIRECT_URI,
  });

  req.session.destroy(() => {
    res.redirect(logoutUrl);
  });
});

// 홈
app.get('/oidc/home', (req, res) => {
  const claims = req.session?.claims;

  if (claims) {
    res.send(`
      <h1>SP2 홈</h1>
      <p>환영합니다, ${claims.preferred_username}님</p>
      
      <a href="/oidc/profile">프로필</a>
      <a href="/oidc/logout">로그아웃</a>
      <button id="agentBtn" type="button">agent 호출(로컬)</button>
      <pre id="agentResult" style="background:#f7f7f7;padding:8px;border:1px solid #ddd;white-space:pre-wrap;"></pre>
      <script>
        (function(){
          const origin='${ORIGIN}';
          const out = document.getElementById('agentResult');
          document.getElementById('agentBtn').addEventListener('click', async () => {
            out.textContent = '요청 중...';
            try {
              // 1) 서버에서 1회용 챌린지 획득 (서버는 비밀키를 노출하지 않음)
              const ch = await fetch('/api/agent/challenge', { cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } })
                .then(r => r.ok ? r.json() : Promise.reject(new Error('challenge failed')));
              // 2) 브라우저가 로컬 에이전트를 직접 호출 (127.0.0.1)
              const r = await fetch('http://127.0.0.1:5400/status', {
                method: 'GET',
                headers: {
                  // 에이전트는 정적 키 대신 아래 1회용 서명/논스를 검증하도록 구현되어야 함
                  'X-SSO-Agent-Key': '${process.env.SSO_AGENT_KEY}',
                  'X-SSO-Nonce': ch.nonce,
                  'X-SSO-Signature': ch.signature,
                  'X-SSO-Origin': origin
                }
              });
              const text = await r.text();
              try {
                const j = JSON.parse(text);
                out.textContent = JSON.stringify(j, null, 2);
              } catch {
                out.textContent = text;
              }
            } catch (e) {
              out.textContent = '에러: ' + (e && e.message ? e.message : e);
            }
          });
        })();
      </script>
    `);
  } else {
    res.send(`
      <h1>SP1 홈</h1>
      <a href="/oidc/login">로그인</a>
    `);
  }
});

app.get('/oidc/profile', (req, res) => {
  const claims = req.session?.claims;
  if (claims) {
    res.send(`
      <h1>사용자 정보</h1>
      <ul>
        <li><strong>사용자명:</strong> ${claims.preferred_username}</li>
        <li><strong>이메일:</strong> ${claims.email || '없음'}</li>
        <li><strong>이름:</strong> ${claims.name || '없음'}</li>
        <li><strong>서브 (sub):</strong> ${claims.sub}</li>
      </ul>
      <a href="/oidc/home">홈</a> | <a href="/oidc/logout">로그아웃</a>
    `);
  } else {
    res.redirect('/oidc/login');
  }
});

app.listen(port, () => {
  console.log(`✅ http://localhost:${port} 에서 실행 중`);
});


// 백채널 로그아웃 엔드포인트
const jwt = require('jsonwebtoken');
const jwksClient = require('jwks-rsa');

// Keycloak JWKS 설정
const keycloakJwksUri = `${process.env.OIDC_ISSUER_URL}/protocol/openid-connect/certs`;
const clientJWKS = jwksClient({
  jwksUri: keycloakJwksUri
});

// 키 가져오는 함수
function getKey(header, callback) {
  clientJWKS.getSigningKey(header.kid, function(err, key) {
    const signingKey = key.getPublicKey();
    callback(null, signingKey);
  });
}

// 백채널 로그아웃 엔드포인트
app.post('/backchannel_logout', express.text(), (req, res) => {
    const logoutToken = req.body.logout_token; // 수정됨
    console.log('📩 백채널 로그아웃 요청 수신');
    if (!logoutToken) {
        console.error('❌ 로그아웃 토큰이 없습니다');
        return res.sendStatus(400);
    }

    jwt.verify(logoutToken, getKey, {
        algorithms: ['RS256'],
        issuer: process.env.OIDC_ISSUER_URL,    
    }, (err, decoded) => {

        if (err) {
            console.error('❌ 로그아웃 토큰 검증 실패:', err.message);
            return res.sendStatus(400);
        }

        console.log('✅ 로그아웃 토큰 검증 완료:', decoded);

        // sub 또는 sid 등을 기준으로 세션 정리 로직 구현 필요
        const sid = decoded.sid;
        if (!sid) {
            console.warn('❗ sid 없음. 세션 추적 불가');
            return res.sendStatus(200);
        }

        // 예시: 메모리 세션 또는 DB에서 해당 sid로 사용자 세션 제거
        console.log(`🧹 세션 ID ${sid}에 대해 로그아웃 처리합니다`);

        res.sendStatus(200);
    });
});
