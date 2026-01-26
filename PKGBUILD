pkgname="dropme"
pkgver="1.0.0"
pkgrel="1"
pkgdesc="Peer to peer secure file transfer tool"
arch=("x86_64")
depends=("dotnet-host" "dotnet-runtime")
makedepends=("dotnet-sdk" "git")
checkdepends=("xorg-server-xvfb" "dotnet-runtime" "libx11" "libice" "libsm" "fontconfig" "gnu-free-fonts")
source=("git+https://gitlab.scss.tcd.ie/sweng26_group3/sweng26_group3.git")
sha256sums=("SKIP")
license=("custom")

build() {
    cd ${srcdir}/sweng26_group3/DropMe.Desktop
    dotnet publish -c release --os linux --arch x64
}

check() {
    xvfb-run ${srcdir}/sweng26_group3/DropMe.Desktop/bin/release/net10.0/linux-x64/publish/DropMe.Desktop &
    APP_PID=$!

    sleep 2

    kill $APP_PID
}

package() {
    depends=("glibc" "libx11" "libice" "libsm" "fontconfig")

    install -d "${pkgdir}/usr/lib/DropMe"
    cp ${srcdir}/sweng26_group3/DropMe.Desktop/bin/release/net10.0/linux-x64/publish/* ${pkgdir}/usr/lib/DropMe/
    rm ${pkgdir}/usr/lib/DropMe/*.pdb

    install -m 755 "${srcdir}/sweng26_group3/DropMe.Desktop/bin/release/net10.0/linux-x64/publish/DropMe.Desktop" "${pkgdir}/usr/lib/DropMe/DropMe.Desktop"

    install -d "${pkgdir}/usr/bin"
    ln -s ../lib/DropMe/DropMe.Desktop ${pkgdir}/usr/bin/dropme
}