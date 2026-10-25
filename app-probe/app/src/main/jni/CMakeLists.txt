cmake_minimum_required(VERSION 3.22.1)
project(dlopen_probe LANGUAGES C)
add_library(dlopen_probe SHARED dlopen_probe.c)
