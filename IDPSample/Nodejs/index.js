// server.js
require('dotenv').config();
const fs = require('fs');
const https = require('https');
const http = require('http');
const path = require('path');
const express = require('express');
const session = require('express-session');
const { Issuer, generators } = require('openid-client');
const jwt = require('jsonwebtoken');
const jwksClient = require('jwks-rsa');

// (선택) 외부 라우트
// const signupRoute = require('./routes/signup');
// const userRoute = require('./routes/user');

const app = express();
app.set('trust proxy', 1);

// ====== 환경 ======
const HTTPS_PORT = 5443;
const HTTP_PORT  = 5001; // http→https 리다이렉트용(선택)
const TLS_KEY  = process.env.TLS_KEY  || path.join(__dirname, 'utils/certs/key.pem');
const TLS_CERT = process.env.TLS_CERT || path.join(__dirname, 'utils/certs/cert.pem');
const ORIGIN   = process.env.PUBLIC_ORIGIN || `https://localhost:${HTTPS_PORT}`;

// ====== 미들웨어 ======
app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use(session({
  secret: 'my-secret-key',
  resave: false,
  saveUninitialized: true,
  cookie: {
    secure: true,     // HTTPS 필수
    httpOnly: true,
    sameSite: 'lax'
  }
}));

// ====== 보호 미들웨어 ======
app.use((req, res, next) => {
  const excluded = [
    '/oidc/login',
    '/oidc/callback',
    '/oidc/logout',
    '/oidc/home',
    '/favicon.ico',
    '/backchannel_logout',
    '/api/me' // 로그인 후 프런트 검증용이면 제외해도 됨(여기선 보호 안 함)
    // '/auth/signup', '/users' 등 필요시 추가
  ];
  if (excluded.some(p => req.path.startsWith(p))) return next();
  if (req.session?.claims) return next();
  return res.redirect('/oidc/login');
});

// ====== 라우트(원하면 주석 해제)
// app.use('/auth', signupRoute);
// app.use('/users', userRoute);

let client;

// ====== OIDC 초기화 ======
async function initOIDC() {
  console.log('🔍 OIDC_ISSUER_URL:', process.env.OIDC_ISSUER_URL);
  const oidcIssuer = await Issuer.discover(process.env.OIDC_ISSUER_URL); // http://localhost:5500/realms/xxx
  console.log('✅ Issuer:', oidcIssuer.issuer);

  client = new oidcIssuer.Client({
    client_id: process.env.OIDC_CLIENT_ID,
    token_endpoint_auth_method: 'none', // PKCE (공개 클라이언트)
    redirect_uris: [process.env.REDIRECT_URI],            // https://localhost:5443/oidc/callback
    post_logout_redirect_uris: [process.env.POST_LOGOUT_REDIRECT_URI],
    response_types: ['code']
  });
}
initOIDC().catch(e => { console.error(e); process.exit(1); });

// ====== 로그인 시작(팝업/일반 공용) ======
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

// ====== 로그아웃 ======
app.get('/oidc/logout', (req, res) => {
  const logoutUrl = client.endSessionUrl({
    id_token_hint: req.session.tokenSet?.id_token,
    post_logout_redirect_uri: process.env.POST_LOGOUT_REDIRECT_URI
  });
  req.session.destroy(() => res.redirect(logoutUrl));
});

// ====== 홈(로그인 후 사용자 정보 일부 노출) ======
app.get('/oidc/home', (req, res) => {
  const claims = req.session?.claims;
  const ui = req.session?.userinfo || {};
  if (claims) {
    res.send(`
      <h1>SP1 홈 (HTTPS)</h1>
      <p>환영합니다, ${claims.preferred_username || ui.preferred_username || '사용자'}님</p>
      <ul>
        <li><b>이메일:</b> ${claims.email ?? ui.email ?? '없음'}</li>
        <li><b>이름:</b> ${claims.name  ?? ui.name  ?? '없음'}</li>
        <li><b>sub:</b> ${claims.sub}</li>
      </ul>
      <a href="/oidc/profile">프로필 상세</a> |
      <a href="/oidc/logout">로그아웃</a>
    `);
  } else {
    res.send(`
      <h1>SP1 홈 (HTTPS)</h1>
      <button onclick="location.href='/oidc/login'">일반 로그인</button>
      <button id="popup">팝업 로그인</button>
      <script>
        const origin='${ORIGIN}';
        document.getElementById('popup').addEventListener('click', () => {
          const w = window.open('/oidc/login?mode=popup', 'kc-login',
            'width=500,height=700,menubar=no,toolbar=no,status=no');
          window.addEventListener('message', (e) => {
            console.log('[message] from:', e.origin, 'data:', e.data); // 🔎 디버그
            if (e.origin !== origin) return; // 오리진 반드시 일치해야 통과

            if (e.data && e.data.type === 'oidc:done') {
              // 세션이 실제로 보이는지 확인 후 홈으로
              fetch('/api/me', { cache: 'no-store' })
                .then(r => r.ok ? location.replace('/oidc/home') : location.reload());
            }
          }, { once: true });
        });
      </script>
    `);
  }
});

// ====== 프로필 상세(Claims + UserInfo 모두 노출) ======
app.get('/oidc/profile', (req, res) => {
  const c  = req.session?.claims;
  const ui = req.session?.userinfo || {};
  if (!c) return res.redirect('/oidc/login');

  const esc = (o) => JSON.stringify(o, null, 2)
    .replace(/[&<>]/g, s => ({'&':'&amp;','<':'&lt;','>':'&gt;'}[s]));

  res.send(`
    <h1>사용자 정보</h1>
    <h2>ID Token Claims</h2>
    <pre>${esc(c)}</pre>
    <h2>UserInfo</h2>
    <pre>${esc(ui)}</pre>
    <a href="/oidc/home">홈</a> | <a href="/oidc/logout">로그아웃</a>
  `);
});

// ====== JSON API (프런트앱에서 활용) ======
app.get('/api/me', (req, res) => {
  const c  = req.session?.claims;
  if (!c) return res.status(401).json({ error: 'unauthenticated' });
  res.json({
    claims: req.session.claims,
    userinfo: req.session.userinfo || null,
    tokenSet: req.session.tokenSet || null   // 필요 시 토큰 제거/마스킹
  });
});

// ====== 백채널 로그아웃 ======
const keycloakJwksUri = `${process.env.OIDC_ISSUER_URL}/protocol/openid-connect/certs`;
const clientJWKS = jwksClient({ jwksUri: keycloakJwksUri });

function getKey(header, cb) {
  clientJWKS.getSigningKey(header.kid, (err, key) => {
    if (err) return cb(err);
    cb(null, key.getPublicKey());
  });
}

// Keycloak은 보통 application/x-www-form-urlencoded 로 보냄 → 안전하게 파싱
app.post('/backchannel_logout', express.text({ type: '*/*' }), (req, res) => {
  let logoutToken;
  if (typeof req.body === 'string') {
    const p = new URLSearchParams(req.body);
    logoutToken = p.get('logout_token');
  } else {
    logoutToken = req.body?.logout_token;
  }
  if (!logoutToken) return res.sendStatus(400);

  jwt.verify(logoutToken, getKey, {
    algorithms: ['RS256'],
    issuer: process.env.OIDC_ISSUER_URL
  }, (err, decoded) => {
    if (err) return res.sendStatus(400);
    const sid = decoded.sid;
    if (sid) {
      // TODO: sid 로 세션 정리
      console.log(`🧹 세션 정리 sid=${sid}`);
    }
    res.sendStatus(200);
  });
});

// ====== 서버 시작 ======
const key = fs.readFileSync(TLS_KEY);
const cert = fs.readFileSync(TLS_CERT);
https.createServer({ key, cert }, app).listen(HTTPS_PORT, () => {
  console.log(`✅ HTTPS: ${ORIGIN}`);
});

// (선택) http 들어오면 https로 리다이렉트
http.createServer((req, res) => {
  const host = (req.headers.host || '').replace(/:\d+$/, '');
  res.writeHead(301, { Location: `https://${host}:${HTTPS_PORT}${req.url}` });
  res.end();
}).listen(HTTP_PORT, () => {
  console.log(`↪️  HTTP→HTTPS redirect: http://localhost:${HTTP_PORT}`);
});
