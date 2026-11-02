Name:           melotag
Version:        __VERSION__
Release:        1%{?dist}
Summary:        音签 Melotag - 跨平台音乐标签编辑器

License:        MIT
URL:            https://github.com/zdxiaoda/Melotag
BuildArch:      x86_64

# Melotag 是 .NET 单文件包；默认的 brp-strip 会把内嵌运行时/程序集剥掉。
%global debug_package %{nil}
%define __strip /bin/true

Requires:       glibc fontconfig freetype libICE libSM libX11 libxcb libxkbcommon
Requires:       libXrandr libXi libXcursor libXinerama mesa-libEGL mesa-libGL glib2
Recommends:     dotnet-runtime-10.0

%description
音签 Melotag - 跨平台音乐标签编辑器。
支持 MP3 / FLAC / APE / OGG / MP4 等容器的标签、封面与歌词读写，
可从多个在线源匹配元数据，并带有重命名、历史撤销与简繁转换。
需要 .NET 10 运行时（本包不包含）。

%prep
# 使用预编译产物，无需解包源码

%build
# 使用预编译产物，无需构建

%install
mkdir -p %{buildroot}/usr/bin
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons/hicolor/512x512/apps
mkdir -p %{buildroot}/usr/share/licenses/melotag
install -m755 __ROOT__/artifacts/publish-linux-x64/Melotag %{buildroot}/usr/bin/melotag
install -m644 __ROOT__/docs/branding/melotag-logo.png %{buildroot}/usr/share/icons/hicolor/512x512/apps/melotag.png
install -m644 __ROOT__/dist/melotag-rpm.desktop %{buildroot}/usr/share/applications/melotag.desktop
install -m644 __ROOT__/LICENSE %{buildroot}/usr/share/licenses/melotag/LICENSE

%files
/usr/bin/melotag
/usr/share/applications/melotag.desktop
/usr/share/icons/hicolor/512x512/apps/melotag.png
/usr/share/licenses/melotag/LICENSE
