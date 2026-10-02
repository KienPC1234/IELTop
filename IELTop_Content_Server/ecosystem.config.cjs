const path = require('path');
const os = require('os');

const serverDir = __dirname;
const publishDll = path.join(serverDir, 'publish', 'IELTop_Content_Server.dll');
const logDir = path.join(os.homedir(), '.pm2', 'logs');
const host = process.env.HOST || '0.0.0.0';
const port = process.env.PORT || '5074';
const dotnetBin = process.env.DOTNET_BIN || 'dotnet';

module.exports = {
  apps: [
    {
      name: 'ieltop-server',
      script: dotnetBin,
      args: `${publishDll} --urls http://${host}:${port}`,
      cwd: serverDir,
      env: {
        ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT || 'Production',
        DOTNET_PRINT_TELEMETRY_MESSAGE: 'false'
      },
      instances: 1,
      autorestart: true,
      watch: false,
      max_memory_restart: '500M',
      time: true,
      log_date_format: 'YYYY-MM-DD HH:mm:ss Z',
      error_file: path.join(logDir, 'ieltop-server-error.log'),
      out_file: path.join(logDir, 'ieltop-server-out.log')
    }
  ]
};
