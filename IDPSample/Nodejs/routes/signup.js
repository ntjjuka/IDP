// routes/signup.js
const express = require('express');
const router = express.Router();
const { createKeycloakUser } = require('../services/keycloak-user');

router.post('/signup', async (req, res) => {
  const { username, email, password } = req.body;

  try {
    const result = await createKeycloakUser({ username, email, password });
    res.json({ success: true, userId: result.userId });
  } catch (err) {
    console.error(err);
    res.status(500).json({ success: false, message: '회원가입 실패' });
  }
});

module.exports = router;
