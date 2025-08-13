// routes/user.js
const express = require('express');
const router = express.Router();
const { findKeycloakUsers, getKeycloakUserById, getUserSessions } = require('../services/keycloak-user');

// 사용자 검색 (username 등으로)
router.get('/', async (req, res) => {
  try {
    const users = await findKeycloakUsers(req.query); // ?username=abc
    res.json(users);
  } catch (err) {
    console.error(err);
    res.status(500).json({ message: '사용자 조회 실패' });
  }
});

// 특정 사용자 ID로 조회
router.get('/:id', async (req, res) => {
  try {
    const user = await getKeycloakUserById(req.params.id);
    res.json(user);
  } catch (err) {
    console.error(err);
    res.status(500).json({ message: '사용자 상세 조회 실패' });
  }
});

// 특정 사용자 세션 조회
router.get('/:id/sessions', async (req, res) => {
  try {
    const sessions = await getUserSessions(req.params.id);
    res.json(sessions);
  } catch (err) {
    console.error(err);
    res.status(500).json({ message: '사용자 세션 조회 실패' });
  }
});
module.exports = router;
