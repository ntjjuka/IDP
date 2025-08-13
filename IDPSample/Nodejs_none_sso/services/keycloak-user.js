// services/keycloak-user.js
const axios = require('axios');
const { getAdminAccessToken } = require('../utils/keycloak-token');
const IDP_HOST = process.env.IDP_HOST
const BASE_URL = IDP_HOST + '/admin/realms/S1'; // your realm

// 사용자 생성
async function createKeycloakUser({ username, email, password }) {
  const token = await getAdminAccessToken();

  // 1. 유저 생성
  const createUserRes = await axios.post(`${BASE_URL}/users`, {
    username,
    email,
    enabled: true
  }, {
    headers: {
      Authorization: `Bearer ${token}`
    }
  });

  if (createUserRes.status !== 201) {
    throw new Error('Failed to create user');
  }

  // 2. 유저 ID 추출
  const location = createUserRes.headers.location;
  const userId = location.split('/').pop();

  // 3. 비밀번호 설정
  await axios.put(`${BASE_URL}/users/${userId}/reset-password`, {
    type: 'password',
    value: password,
    temporary: false
  }, {
    headers: {
      Authorization: `Bearer ${token}`
    }
  });

  return { userId };
}


// 사용자 검색 (username 등으로)
async function findKeycloakUsers(query = {}) {
  const token = await getAdminAccessToken();

  const response = await axios.get(`${BASE_URL}/users`, {
    headers: { Authorization: `Bearer ${token}` },
    params: query // { username: 'testuser' } 등
  });

  return response.data;
}

// 특정 사용자 ID로 조회
async function getKeycloakUserById(userId) {
  const token = await getAdminAccessToken();

  const response = await axios.get(`${BASE_URL}/users/${userId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  return response.data;
}

// 세션 조회 (관리자 권한 필요)
async function getUserSessions(userId) {
  const token = await getAdminAccessToken(); 
  const response = await axios.get(`${BASE_URL}/users/${userId}/sessions`, {
      headers: { Authorization: `Bearer ${token}` },
    }
  );

  return response.data; // 배열. 길이가 0이면 세션 없음
}

module.exports = { 
  createKeycloakUser, 
  findKeycloakUsers, 
  getKeycloakUserById,
  getUserSessions,
 };
