# ProxySQL Admin UI

一个用于管理 [ProxySQL](https://www.proxysql.com/) 配置的 Web 管理界面，基于 Blazor Server 和 .NET 8 构建。

## 功能

- 查看 ProxySQL 仪表盘和统计信息
- 新增、编辑、删除和刷新 `mysql_servers`
- 新增、编辑、删除和刷新 `mysql_users`
- 配置查询规则
- 查看查询摘要统计并进行筛选
- 配置全局变量
- 修改服务器和用户后自动执行 `LOAD ... TO RUNTIME` 与 `SAVE ... TO DISK`
- 提供 `/health` 健康检查端点

> [!WARNING]
> 本应用可以修改线上 ProxySQL 的路由和认证配置。生产环境请限制 Web 界面访问来源，使用专用 ProxySQL Admin 账号，在变更前核对 Runtime 和 Disk 配置，并准备回滚 SQL。

## 快速开始

启动镜像，并配置 ProxySQL Admin 连接：

```bash
docker run -d --restart unless-stopped \
  --name proxysql-admin-ui \
  -p 8001:8001 \
  -e PAI_ConnectionStrings__ProxySqlContext='Server=host.docker.internal;Port=6032;Uid=radmin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;' \
  -e PAI_DefaultUsers__0__Username=admin \
  -e PAI_DefaultUsers__0__Password=CHANGE_ME_TOO \
  amwpfiqvy/proxysql-admin-ui:latest
```

启动后访问 `http://localhost:8001`。

如果应用和 ProxySQL 位于同一个 Docker Compose 网络，可以使用服务名连接：

```yaml
services:
  proxysql-admin-ui:
    image: amwpfiqvy/proxysql-admin-ui:latest
    restart: unless-stopped
    ports:
      - "8001:8001"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8001
      PAI_ConnectionStrings__ProxySqlContext: Server=proxysql;Port=6032;Uid=radmin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;
      PAI_DefaultUsers__0__Username: admin
      PAI_DefaultUsers__0__Password: CHANGE_ME_TOO
```

## 配置项

| 环境变量 | 说明 |
|---|---|
| `PAI_ConnectionStrings__ProxySqlContext` | ProxySQL Admin 连接字符串 |
| `PAI_DefaultUsers__0__Username` | 首次创建的 Web 管理员用户名 |
| `PAI_DefaultUsers__0__Password` | 首次创建的 Web 管理员密码 |
| `ASPNETCORE_URLS` | Web 监听地址，镜像默认使用 `8001` 端口 |

Web 管理员身份数据库存放在应用数据目录中。需要在替换容器后保留用户数据时，请将持久化卷挂载到 `/app/data`。

## 支持架构

| 架构 | 状态 |
|---|---|
| `linux/amd64` | 支持 |
| `linux/arm64` | 支持 |

## 相关链接

- 源码：https://github.com/Apq/proxysql-admin-ui
- 许可证：MIT
