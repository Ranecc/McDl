# 更新日志

本文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### 添加

- 添加：整合包游戏本体下载
- 添加：下载引擎 / 图形界面 / 命令行的分层重构，`McDl.Core` 不再依赖界面层

### 变更

- 变更：命令行与图形界面共用同一套下载与解析逻辑

## [0.1.0] - 2026-10-20

首个公开版本。

### 添加

- 添加：WinForms 图形界面，通过 Costura 打包为单文件 `McDl.exe`
- 添加：版本列表拉取与 `全部 / 正式版 / 快照` 过滤、关键字搜索
- 添加：并行下载（默认 8 线程可调）与逐文件 SHA1 校验，已存在且校验通过的文件自动跳过
- 添加：基于 `.part` 临时文件的断点续传，取消或中断后可从已下载字节继续
- 添加：单文件重试（指数退避）与镜像回退（官方优先，失败后按惩罚权重切换到 BMCLAPI）
- 添加：`legacy` / `pre-1.6` 虚拟资源树展开
- 添加：natives 解压，兼容旧式 classifiers 映射与新式 `:natives-windows` 命名，并过滤 `META-INF`
- 添加：命令行入口，支持 `list` / `plan` / `install` 子命令
