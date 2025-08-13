// utils/keycloak-token.js
const axios = require('axios');


async function getAdminAccessToken() {
    const IDP_HOST = process.env.IDP_HOST
    const tokenUrl = IDP_HOST + '/realms/master/protocol/openid-connect/token';

    const params = new URLSearchParams();
    params.append('client_id', 'cpp_sp_app');         // 또는 별도 관리용 client_id
    params.append('username', 'sp_master');              // 관리자 계정
    params.append('password', '1q2w3e4r%T');              // 관리자 비밀번호
    params.append('grant_type', 'password');

    try {
        const response = await axios.post(tokenUrl, params);
        return response.data.access_token;
    } catch (err) {
        console.error('Failed to get admin token:', err.response?.data || err.message);
        throw err;
    }
}

module.exports = { getAdminAccessToken };
